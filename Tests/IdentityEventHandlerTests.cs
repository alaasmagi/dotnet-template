using Application;
using Base.Contracts.DTO;
using Base.DTO;
using Contracts.Application;
using Domain;

namespace Tests;

public class IdentityEventHandlerTests
{
    private FakeAppUserService _appUserService = default!;
    private IdentityEventHandler _handler = default!;

    [SetUp]
    public void SetUp()
    {
        _appUserService = new FakeAppUserService();
        _handler = new IdentityEventHandler(_appUserService);
    }

    private static AppUser User() => AppUser.FromIdentity(Guid.NewGuid(), "user@example.com", "user", "Full Name", null, null);

    [TestCase("user-created")]
    [TestCase("user-updated")]
    [TestCase("user-enabled")]
    [TestCase("user-disabled")]
    public async Task HandleAsync_UserSyncAction_SyncsUserWithMessageId(string action)
    {
        var messageId = Guid.NewGuid();
        var appUser = User();

        var result = await _handler.HandleAsync(messageId.ToString(), action, "1.0", appUser);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(MessageHandlingResult.Applied));
            Assert.That(_appUserService.MessageId, Is.EqualTo(messageId));
            Assert.That(_appUserService.Synced, Is.SameAs(appUser));
            Assert.That(_appUserService.RemovedId, Is.Null);
        });
    }

    [Test]
    public async Task HandleAsync_UserDeleted_RemovesUser()
    {
        var appUser = User();

        var result = await _handler.HandleAsync(Guid.NewGuid().ToString(), "user-deleted", "1.0", appUser);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(MessageHandlingResult.Applied));
            Assert.That(_appUserService.RemovedId, Is.EqualTo(appUser.Id));
            Assert.That(_appUserService.Synced, Is.Null);
        });
    }

    [Test]
    public async Task HandleAsync_DuplicateMessage_ReturnsAlreadyProcessed()
    {
        _appUserService.Result = MethodResponse<bool>.Success(false);

        var result = await _handler.HandleAsync(Guid.NewGuid().ToString(), "user-created", "1.0", User());

        Assert.That(result, Is.EqualTo(MessageHandlingResult.AlreadyProcessed));
    }

    [Test]
    public async Task HandleAsync_UnsupportedAction_IsIgnoredBeforeValidation()
    {
        var result = await _handler.HandleAsync("not-a-guid", "email-user-verify", "9.0", null);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(MessageHandlingResult.Ignored));
            Assert.That(_appUserService.MessageId, Is.Null);
        });
    }

    [Test]
    public async Task HandleAsync_UnknownContentVersion_Rejects()
    {
        var result = await _handler.HandleAsync(Guid.NewGuid().ToString(), "user-created", "2.0", User());

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(MessageHandlingResult.Reject));
            Assert.That(_appUserService.MessageId, Is.Null);
        });
    }

    [Test]
    public async Task HandleAsync_UnreadableContent_Rejects()
    {
        var result = await _handler.HandleAsync(Guid.NewGuid().ToString(), "user-created", "1.0", null);

        Assert.That(result, Is.EqualTo(MessageHandlingResult.Reject));
    }

    [Test]
    public async Task HandleAsync_InvalidMessageId_Rejects()
    {
        var result = await _handler.HandleAsync("not-a-guid", "user-created", "1.0", User());

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(MessageHandlingResult.Reject));
            Assert.That(_appUserService.MessageId, Is.Null);
        });
    }

    [Test]
    public async Task HandleAsync_ServiceFailure_Rejects()
    {
        _appUserService.Result = MethodResponse<bool>.Failure(new Error(ErrorDefaults.Codes.SaveFailed, ErrorDefaults.Messages.SaveFailed));

        var result = await _handler.HandleAsync(Guid.NewGuid().ToString(), "user-created", "1.0", User());

        Assert.That(result, Is.EqualTo(MessageHandlingResult.Reject));
    }

    [TestCase("user-updated")]
    [TestCase("user-deleted")]
    public async Task HandleAsync_ConcurrencyConflict_Retries(string action)
    {
        _appUserService.Result = MethodResponse<bool>.Failure(
            new Error(ErrorDefaults.Codes.ConcurrencyConflict, ErrorDefaults.Messages.ConcurrencyConflict));

        var result = await _handler.HandleAsync(Guid.NewGuid().ToString(), action, "1.0", User());

        Assert.That(result, Is.EqualTo(MessageHandlingResult.Retry));
    }

    private class FakeAppUserService : IAppUserService
    {
        public Guid? MessageId { get; private set; }
        public AppUser? Synced { get; private set; }
        public Guid? RemovedId { get; private set; }
        public IMethodResponse<bool> Result { get; set; } = MethodResponse<bool>.Success(true);

        public Task<IMethodResponse<bool>> SyncFromIdentityAsync(Guid messageId, AppUser appUser)
        {
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
