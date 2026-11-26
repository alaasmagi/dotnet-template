using System.Data.Common;
using System.Text.Json;
using Base.Contracts.DTO;
using Base.Contracts.Message;
using Base.DTO;
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
    private FakeAppUserService _appUserService = default!;
    private RabbitMqEventHandler _handler = default!;

    [SetUp]
    public void SetUp()
    {
        _appUserService = new FakeAppUserService();
        var provider = new ServiceCollection()
            .AddScoped<IAppUserService>(_ => _appUserService)
            .BuildServiceProvider();
        _handler = new RabbitMqEventHandler(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<RabbitMqEventHandler>.Instance);
    }

    private static IBaseEventEnvelope<JsonElement> Envelope(string source, string action, object content, string contentVersion = "1.0")
    {
        return BaseEventEnvelope<JsonElement>.Create(source, "my-realm", action, contentVersion, JsonSerializer.SerializeToElement(content));
    }

    [TestCase("user-created")]
    [TestCase("user-updated")]
    [TestCase("user-enabled")]
    [TestCase("user-disabled")]
    public async Task HandleAsync_IdentityUserEvent_SyncsUserWithMessageIdAndAcks(string action)
    {
        var userId = Guid.NewGuid();
        var envelope = Envelope(DefaultSources.IdentityHub, action,
            new { userId, email = " user@example.com ", username = "user", fullName = "Full Name", locale = "en-GB", timeZone = "Europe/London" });

        var disposition = await _handler.HandleAsync(envelope, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(disposition, Is.EqualTo(MessageDisposition.Ack));
            Assert.That(_appUserService.MessageId, Is.EqualTo(Guid.Parse(envelope.Id)));
            Assert.That(_appUserService.Synced?.Id, Is.EqualTo(userId));
            Assert.That(_appUserService.Synced?.Email, Is.EqualTo("user@example.com"));
            Assert.That(_appUserService.Synced?.Locale, Is.EqualTo("en-GB"));
            Assert.That(_appUserService.Synced?.TimeZone, Is.EqualTo("Europe/London"));
        });
    }

    [Test]
    public async Task HandleAsync_IdentityUserDeleted_RemovesUserAndAcks()
    {
        var userId = Guid.NewGuid();

        var disposition = await _handler.HandleAsync(Envelope(DefaultSources.IdentityHub, "user-deleted", new { userId }), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(disposition, Is.EqualTo(MessageDisposition.Ack));
            Assert.That(_appUserService.RemovedId, Is.EqualTo(userId));
        });
    }

    [Test]
    public async Task HandleAsync_DuplicateMessage_Acks()
    {
        _appUserService.Result = MethodResponse<bool>.Success(false);

        var disposition = await _handler.HandleAsync(
            Envelope(DefaultSources.IdentityHub, "user-created", new { userId = Guid.NewGuid() }),
            CancellationToken.None);

        Assert.That(disposition, Is.EqualTo(MessageDisposition.Ack));
    }

    [Test]
    public async Task HandleAsync_TransientDatabaseFailure_Requeues()
    {
        _appUserService.Exception = new InvalidOperationException("wrapped", new FakeDbException(isTransient: true));

        var disposition = await _handler.HandleAsync(
            Envelope(DefaultSources.IdentityHub, "user-created", new { userId = Guid.NewGuid() }),
            CancellationToken.None);

        Assert.That(disposition, Is.EqualTo(MessageDisposition.Requeue));
    }

    [Test]
    public void HandleAsync_NonTransientDatabaseFailure_Throws()
    {
        _appUserService.Exception = new FakeDbException(isTransient: false);

        Assert.ThrowsAsync<FakeDbException>(() => _handler.HandleAsync(
            Envelope(DefaultSources.IdentityHub, "user-created", new { userId = Guid.NewGuid() }),
            CancellationToken.None));
    }

    [Test]
    public async Task HandleAsync_InvalidUserId_Rejects()
    {
        var disposition = await _handler.HandleAsync(
            Envelope(DefaultSources.IdentityHub, "user-created", new { userId = "not-a-guid" }),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(disposition, Is.EqualTo(MessageDisposition.Reject));
            Assert.That(_appUserService.Synced, Is.Null);
        });
    }

    [Test]
    public async Task HandleAsync_ContentOfWrongShape_Rejects()
    {
        var disposition = await _handler.HandleAsync(
            Envelope(DefaultSources.IdentityHub, "user-created", new { userId = 42 }),
            CancellationToken.None);

        Assert.That(disposition, Is.EqualTo(MessageDisposition.Reject));
    }

    [Test]
    public async Task HandleAsync_UnknownContentVersion_Rejects()
    {
        var disposition = await _handler.HandleAsync(
            Envelope(DefaultSources.IdentityHub, "user-created", new { userId = Guid.NewGuid() }, contentVersion: "2.0"),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(disposition, Is.EqualTo(MessageDisposition.Reject));
            Assert.That(_appUserService.Synced, Is.Null);
        });
    }

    [Test]
    public async Task HandleAsync_ServiceFailure_Rejects()
    {
        _appUserService.Result = MethodResponse<bool>.Failure(new Error(ErrorDefaults.Codes.SaveFailed, ErrorDefaults.Messages.SaveFailed));

        var disposition = await _handler.HandleAsync(
            Envelope(DefaultSources.IdentityHub, "user-created", new { userId = Guid.NewGuid() }),
            CancellationToken.None);

        Assert.That(disposition, Is.EqualTo(MessageDisposition.Reject));
    }

    [Test]
    public async Task HandleAsync_UnsupportedIdentityAction_Acks()
    {
        var disposition = await _handler.HandleAsync(
            Envelope(DefaultSources.IdentityHub, "email-user-verify", new { userId = Guid.NewGuid() }, contentVersion: "9.0"),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(disposition, Is.EqualTo(MessageDisposition.Ack));
            Assert.That(_appUserService.Synced, Is.Null);
        });
    }

    [Test]
    public async Task HandleAsync_OtherSource_IsIgnoredAndAcked()
    {
        var disposition = await _handler.HandleAsync(
            Envelope("other-service", "user-created", new { userId = Guid.NewGuid() }),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(disposition, Is.EqualTo(MessageDisposition.Ack));
            Assert.That(_appUserService.Synced, Is.Null);
            Assert.That(_appUserService.RemovedId, Is.Null);
        });
    }

    private class FakeDbException(bool isTransient) : DbException
    {
        public override bool IsTransient => isTransient;
    }

    private class FakeAppUserService : IAppUserService
    {
        public Guid? MessageId { get; private set; }
        public AppUser? Synced { get; private set; }
        public Guid? RemovedId { get; private set; }
        public IMethodResponse<bool> Result { get; set; } = MethodResponse<bool>.Success(true);
        public Exception? Exception { get; set; }

        public Task<IMethodResponse<bool>> SyncFromIdentityAsync(Guid messageId, AppUser appUser)
        {
            if (Exception != null)
            {
                throw Exception;
            }

            MessageId = messageId;
            Synced = appUser;
            return Task.FromResult(Result);
        }

        public Task<IMethodResponse<bool>> RemoveFromIdentityAsync(Guid messageId, Guid id)
        {
            MessageId = messageId;
            RemovedId = id;
            return Task.FromResult(Result);
        }

        public Task<IMethodResponse<IEnumerable<AppUser>>> GetAllAsync(Guid actor = default) => throw new NotSupportedException();

        public Task<IMethodResponse<IEnumerable<AppUser>>> GetAllByPageAsync(int pageNr, int pageSize, Guid actor = default) => throw new NotSupportedException();

        public Task<IMethodResponse<int>> GetCountAsync(Guid actor = default) => throw new NotSupportedException();

        public Task<IMethodResponse<bool>> ExistsAsync(Guid id, Guid actor = default) => throw new NotSupportedException();

        public Task<IMethodResponse<AppUser>> GetByIdAsync(Guid id, Guid actor = default) => throw new NotSupportedException();

        public Task<IMethodResponse<AppUser>> CreateAsync(AppUser entity, Guid actor = default) => throw new NotSupportedException();

        public Task<IMethodResponse<AppUser>> UpdateAsync(Guid id, AppUser entity, string? expectedConcurrencyToken = null, Guid actor = default) => throw new NotSupportedException();

        public Task<IMethodResponse<bool>> RemoveAsync(Guid id, string? expectedConcurrencyToken = null, Guid actor = default) => throw new NotSupportedException();
    }
}
