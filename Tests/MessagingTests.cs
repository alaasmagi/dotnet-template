using System.Data.Common;
using System.Text.Json;
using Base.Contracts.Message;
using Base.Message;
using Contracts.Application;
using Domain;
using External.RabbitMQ;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

public class RabbitMqEventPublisherTests
{
    private static readonly AppMessagingOptions Options = new() { Queue = "app-service", AppSlug = "app-service" };

    [Test]
    public async Task PublishAsync_UsesSlugAsSourceAndTenant()
    {
        var basePublisher = new FakeBasePublisher(PublishResult.Ok());
        var publisher = new RabbitMqEventPublisher(basePublisher, Options);

        var result = await publisher.PublishAsync("thing-created", "1.0", new { Name = "thing" });

        Assert.Multiple(() =>
        {
            Assert.That(result.Successful, Is.True);
            Assert.That(basePublisher.RoutingKey, Is.EqualTo("app-service.app-service.thing-created"));
            Assert.That(basePublisher.ContentVersion, Is.EqualTo("1.0"));
        });
    }

    [Test]
    public async Task PublishAsync_BrokerFailure_ReturnsPublishFailed()
    {
        var publisher = new RabbitMqEventPublisher(new FakeBasePublisher(PublishResult.Failed("nack")), Options);

        var result = await publisher.PublishAsync("thing-created", "1.0", new { Name = "thing" });

        Assert.Multiple(() =>
        {
            Assert.That(result.Successful, Is.False);
            Assert.That(result.Error?.Code, Is.EqualTo(RabbitMqEventPublisher.PublishFailedErrorCode));
            Assert.That(result.Error?.Message, Is.EqualTo("nack"));
        });
    }

    [Test]
    public void PublishAsync_InvalidNotificationContent_ThrowsBeforePublishing()
    {
        var basePublisher = new FakeBasePublisher(PublishResult.Ok());
        var publisher = new RabbitMqEventPublisher(basePublisher, Options);
        var content = new BaseNotificationContent
        {
            UserId = Guid.NewGuid().ToString(),
            Email = "user@example.com",
            FullName = "Full Name",
            Locale = "et",
            TimeZone = "Europe/Tallinn"
        };

        Assert.ThrowsAsync<ArgumentException>(() => publisher.PublishAsync("email-thing", "1.0", content));
        Assert.That(basePublisher.PublishCalls, Is.Zero);
    }

    [Test]
    public async Task PublishAsync_ValidNotificationContent_Publishes()
    {
        var basePublisher = new FakeBasePublisher(PublishResult.Ok());
        var publisher = new RabbitMqEventPublisher(basePublisher, Options);
        var content = new BaseNotificationContent
        {
            UserId = Guid.NewGuid().ToString(),
            Email = "user@example.com",
            FullName = "Full Name",
            Locale = "et-EE",
            TimeZone = "Europe/Tallinn"
        };

        var result = await publisher.PublishAsync("email-thing", "1.0", content);

        Assert.Multiple(() =>
        {
            Assert.That(result.Successful, Is.True);
            Assert.That(basePublisher.PublishCalls, Is.EqualTo(1));
        });
    }

    private class FakeBasePublisher(PublishResult result) : IBaseEventPublisher
    {
        public int PublishCalls { get; private set; }
        public string? RoutingKey { get; private set; }
        public string? ContentVersion { get; private set; }

        public Task<PublishResult> PublishAsync<TContent>(
            IBaseEventEnvelope<TContent> message,
            string? expiration = null,
            CancellationToken cancellationToken = default)
        {
            PublishCalls++;
            RoutingKey = BaseRoutingKey.For(message);
            ContentVersion = message.ContentVersion;
            return Task.FromResult(result);
        }
    }
}

public class RabbitMqEventHandlerTests
{
    private FakeIdentityEventHandler _identityEventHandler = default!;
    private RabbitMqEventHandler _handler = default!;

    [SetUp]
    public void SetUp()
    {
        _identityEventHandler = new FakeIdentityEventHandler();
        var provider = new ServiceCollection()
            .AddScoped<IIdentityEventHandler>(_ => _identityEventHandler)
            .BuildServiceProvider();
        _handler = new RabbitMqEventHandler(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<RabbitMqEventHandler>.Instance);
    }

    private static IBaseEventEnvelope<JsonElement> Envelope(string source, string action, object content, string contentVersion = "1.0")
    {
        return BaseEventEnvelope<JsonElement>.Create(source, "my-realm", action, contentVersion, JsonSerializer.SerializeToElement(content));
    }

    [Test]
    public async Task HandleAsync_IdentityEvent_PassesEnvelopeFieldsAndParsedUser()
    {
        var userId = Guid.NewGuid();
        var envelope = Envelope(DefaultSources.IdentityHub, "user-updated",
            new { userId, email = " user@example.com ", username = "user", fullName = "Full Name", locale = "en-GB", timeZone = "Europe/London" });

        await _handler.HandleAsync(envelope, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(_identityEventHandler.MessageId, Is.EqualTo(envelope.Id));
            Assert.That(_identityEventHandler.Action, Is.EqualTo("user-updated"));
            Assert.That(_identityEventHandler.ContentVersion, Is.EqualTo("1.0"));
            Assert.That(_identityEventHandler.AppUser?.Id, Is.EqualTo(userId));
            Assert.That(_identityEventHandler.AppUser?.Email, Is.EqualTo("user@example.com"));
            Assert.That(_identityEventHandler.AppUser?.Locale, Is.EqualTo("en-GB"));
            Assert.That(_identityEventHandler.AppUser?.TimeZone, Is.EqualTo("Europe/London"));
        });
    }

    [Test]
    public async Task HandleAsync_InvalidUserId_PassesNoUser()
    {
        await _handler.HandleAsync(
            Envelope(DefaultSources.IdentityHub, "user-created", new { userId = "not-a-guid" }),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(_identityEventHandler.Called, Is.True);
            Assert.That(_identityEventHandler.AppUser, Is.Null);
        });
    }

    [Test]
    public async Task HandleAsync_ContentOfWrongShape_PassesNoUser()
    {
        await _handler.HandleAsync(
            Envelope(DefaultSources.IdentityHub, "user-created", new { userId = 42 }),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(_identityEventHandler.Called, Is.True);
            Assert.That(_identityEventHandler.AppUser, Is.Null);
        });
    }

    [TestCase(MessageHandlingResult.Applied, MessageDisposition.Ack)]
    [TestCase(MessageHandlingResult.AlreadyProcessed, MessageDisposition.Ack)]
    [TestCase(MessageHandlingResult.Ignored, MessageDisposition.Ack)]
    [TestCase(MessageHandlingResult.Retry, MessageDisposition.Requeue)]
    [TestCase(MessageHandlingResult.Reject, MessageDisposition.Reject)]
    public async Task HandleAsync_MapsHandlingResultToDisposition(MessageHandlingResult result, MessageDisposition expected)
    {
        _identityEventHandler.Result = result;

        var disposition = await _handler.HandleAsync(
            Envelope(DefaultSources.IdentityHub, "user-created", new { userId = Guid.NewGuid() }),
            CancellationToken.None);

        Assert.That(disposition, Is.EqualTo(expected));
    }

    [Test]
    public async Task HandleAsync_TransientDatabaseFailure_Requeues()
    {
        _identityEventHandler.Exception = new InvalidOperationException("wrapped", new FakeDbException(isTransient: true));

        var disposition = await _handler.HandleAsync(
            Envelope(DefaultSources.IdentityHub, "user-created", new { userId = Guid.NewGuid() }),
            CancellationToken.None);

        Assert.That(disposition, Is.EqualTo(MessageDisposition.Requeue));
    }

    [Test]
    public void HandleAsync_NonTransientDatabaseFailure_Throws()
    {
        _identityEventHandler.Exception = new FakeDbException(isTransient: false);

        Assert.ThrowsAsync<FakeDbException>(() => _handler.HandleAsync(
            Envelope(DefaultSources.IdentityHub, "user-created", new { userId = Guid.NewGuid() }),
            CancellationToken.None));
    }

    [Test]
    public async Task HandleAsync_OtherSource_IsAckedWithoutIdentityHandling()
    {
        var disposition = await _handler.HandleAsync(
            Envelope("other-service", "user-created", new { userId = Guid.NewGuid() }),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(disposition, Is.EqualTo(MessageDisposition.Ack));
            Assert.That(_identityEventHandler.Called, Is.False);
        });
    }

    private class FakeDbException(bool isTransient) : DbException
    {
        public override bool IsTransient => isTransient;
    }

    private class FakeIdentityEventHandler : IIdentityEventHandler
    {
        public bool Called { get; private set; }
        public string? MessageId { get; private set; }
        public string? Action { get; private set; }
        public string? ContentVersion { get; private set; }
        public AppUser? AppUser { get; private set; }
        public MessageHandlingResult Result { get; set; } = MessageHandlingResult.Applied;
        public Exception? Exception { get; set; }

        public Task<MessageHandlingResult> HandleAsync(string messageId, string action, string contentVersion, AppUser? appUser)
        {
            if (Exception != null)
            {
                throw Exception;
            }

            Called = true;
            MessageId = messageId;
            Action = action;
            ContentVersion = contentVersion;
            AppUser = appUser;
            return Task.FromResult(Result);
        }
    }
}
