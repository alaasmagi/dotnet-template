using Application;
using Base.Contracts.DataAccess;
using Base.Contracts.DTO;
using Base.DTO;
using Contracts.DataAccess;
using Domain;

namespace Tests;

public class AppUserServiceTests
{
    private FakeAppUserRepository _repository = default!;
    private FakeProcessedMessageRepository _processedMessages = default!;
    private FakeUow _uow = default!;
    private AppUserService _service = default!;

    [SetUp]
    public void SetUp()
    {
        _repository = new FakeAppUserRepository();
        _processedMessages = new FakeProcessedMessageRepository();
        _uow = new FakeUow();
        _service = new AppUserService(_repository, _processedMessages, _uow, new AppUserIdentityMapper());
    }

    [Test]
    public async Task SyncFromIdentityAsync_NewUser_CreatesAndSavesWithMessageId()
    {
        var messageId = Guid.NewGuid();
        var appUser = AppUser.FromIdentity(Guid.NewGuid(), "user@example.com", "user", "Full Name", null, null);

        var result = await _service.SyncFromIdentityAsync(messageId, appUser);

        Assert.Multiple(() =>
        {
            Assert.That(result.Successful, Is.True);
            Assert.That(result.Value, Is.True);
            Assert.That(_repository.Created, Is.SameAs(appUser));
            Assert.That(_repository.Updated, Is.Null);
            Assert.That(_processedMessages.Added, Is.EqualTo(new[] { messageId }));
            Assert.That(_uow.SaveCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task SyncFromIdentityAsync_ExistingUser_UpdatesWithCurrentConcurrencyTokenAndSaves()
    {
        var id = Guid.NewGuid();
        _repository.Stored[id] = new AppUser { Id = id, ConcurrencyToken = "current" };

        var result = await _service.SyncFromIdentityAsync(
            Guid.NewGuid(),
            AppUser.FromIdentity(id, "new@example.com", "user", "Full Name", "et-EE", "Europe/Tallinn"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Successful, Is.True);
            Assert.That(_repository.Created, Is.Null);
            Assert.That(_repository.Updated?.Email, Is.EqualTo("new@example.com"));
            Assert.That(_repository.ExpectedConcurrencyToken, Is.EqualTo("current"));
            Assert.That(_uow.SaveCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task SyncFromIdentityAsync_DuplicateMessageId_ReturnsFalseWithoutChanges()
    {
        var messageId = Guid.NewGuid();
        _processedMessages.Existing.Add(messageId);

        var result = await _service.SyncFromIdentityAsync(messageId, AppUser.FromIdentity(Guid.NewGuid(), null, null, null, null, null));

        Assert.Multiple(() =>
        {
            Assert.That(result.Successful, Is.True);
            Assert.That(result.Value, Is.False);
            Assert.That(_repository.Created, Is.Null);
            Assert.That(_repository.Updated, Is.Null);
            Assert.That(_processedMessages.Added, Is.Empty);
            Assert.That(_uow.SaveCalls, Is.Zero);
        });
    }

    [Test]
    public async Task SyncFromIdentityAsync_SaveRejectedByConcurrentDuplicate_ReturnsFalse()
    {
        var messageId = Guid.NewGuid();
        _uow.SaveResult = MethodResponse<int>.Failure(new Error(ErrorDefaults.Codes.SaveFailed, ErrorDefaults.Messages.SaveFailed));
        _uow.OnSave = () => _processedMessages.Existing.Add(messageId);

        var result = await _service.SyncFromIdentityAsync(messageId, AppUser.FromIdentity(Guid.NewGuid(), null, null, null, null, null));

        Assert.Multiple(() =>
        {
            Assert.That(result.Successful, Is.True);
            Assert.That(result.Value, Is.False);
        });
    }

    [Test]
    public async Task SyncFromIdentityAsync_SaveRejected_ReturnsSaveError()
    {
        _uow.SaveResult = MethodResponse<int>.Failure(new Error(ErrorDefaults.Codes.SaveFailed, ErrorDefaults.Messages.SaveFailed));

        var result = await _service.SyncFromIdentityAsync(Guid.NewGuid(), AppUser.FromIdentity(Guid.NewGuid(), null, null, null, null, null));

        Assert.Multiple(() =>
        {
            Assert.That(result.Successful, Is.False);
            Assert.That(result.Error?.Code, Is.EqualTo(ErrorDefaults.Codes.SaveFailed));
        });
    }

    [Test]
    public async Task RemoveFromIdentityAsync_MissingUser_SavesMessageIdOnly()
    {
        var messageId = Guid.NewGuid();

        var result = await _service.RemoveFromIdentityAsync(messageId, Guid.NewGuid());

        Assert.Multiple(() =>
        {
            Assert.That(result.Successful, Is.True);
            Assert.That(result.Value, Is.True);
            Assert.That(_repository.RemovedId, Is.Null);
            Assert.That(_processedMessages.Added, Is.EqualTo(new[] { messageId }));
            Assert.That(_uow.SaveCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task RemoveFromIdentityAsync_ExistingUser_RemovesWithCurrentConcurrencyTokenAndSaves()
    {
        var id = Guid.NewGuid();
        _repository.Stored[id] = new AppUser { Id = id, ConcurrencyToken = "current" };

        var result = await _service.RemoveFromIdentityAsync(Guid.NewGuid(), id);

        Assert.Multiple(() =>
        {
            Assert.That(result.Successful, Is.True);
            Assert.That(result.Value, Is.True);
            Assert.That(_repository.RemovedId, Is.EqualTo(id));
            Assert.That(_repository.ExpectedConcurrencyToken, Is.EqualTo("current"));
            Assert.That(_uow.SaveCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task RemoveFromIdentityAsync_DuplicateMessageId_ReturnsFalseWithoutChanges()
    {
        var id = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        _repository.Stored[id] = new AppUser { Id = id, ConcurrencyToken = "current" };
        _processedMessages.Existing.Add(messageId);

        var result = await _service.RemoveFromIdentityAsync(messageId, id);

        Assert.Multiple(() =>
        {
            Assert.That(result.Successful, Is.True);
            Assert.That(result.Value, Is.False);
            Assert.That(_repository.RemovedId, Is.Null);
            Assert.That(_uow.SaveCalls, Is.Zero);
        });
    }

    internal class FakeUow : IBaseUow
    {
        public int SaveCalls { get; private set; }
        public IMethodResponse<int> SaveResult { get; set; } = MethodResponse<int>.Success(1);
        public Action? OnSave { get; set; }

        public Task<int> SaveChangesAsync()
        {
            throw new NotSupportedException("Services in this template commit through TrySaveChangesAsync.");
        }

        public Task<IMethodResponse<int>> TrySaveChangesAsync()
        {
            SaveCalls++;
            OnSave?.Invoke();
            return Task.FromResult(SaveResult);
        }
    }

    internal class FakeProcessedMessageRepository : IProcessedMessageRepository
    {
        public HashSet<Guid> Existing { get; } = [];
        public List<Guid> Added { get; } = [];
        public List<Guid> Expired { get; } = [];
        public DateTimeOffset? Cutoff { get; private set; }
        public List<int> RemovedBatchSizes { get; } = [];

        public Task<bool> ExistsAsync(Guid messageId) => Task.FromResult(Existing.Contains(messageId));

        public void Add(Guid messageId) => Added.Add(messageId);

        public Task<IReadOnlyList<Guid>> GetIdsProcessedBeforeAsync(DateTimeOffset cutoff, int count)
        {
            Cutoff = cutoff;
            return Task.FromResult<IReadOnlyList<Guid>>(Expired.Take(count).ToList());
        }

        public void Remove(IEnumerable<Guid> messageIds)
        {
            var removed = messageIds.ToList();
            Expired.RemoveAll(removed.Contains);
            RemovedBatchSizes.Add(removed.Count);
        }
    }

    private class FakeAppUserRepository : IAppUserRepository
    {
        public Dictionary<Guid, AppUser> Stored { get; } = [];
        public AppUser? Created { get; private set; }
        public AppUser? Updated { get; private set; }
        public Guid? RemovedId { get; private set; }
        public string? ExpectedConcurrencyToken { get; private set; }

        public Task<IMethodResponse<AppUser>> GetByIdAsync(Guid id, Guid actor = default)
        {
            IMethodResponse<AppUser> response = Stored.TryGetValue(id, out var appUser)
                ? MethodResponse<AppUser>.Success(appUser)
                : MethodResponse<AppUser>.Failure(new Error(ErrorDefaults.Codes.NotFound, ErrorDefaults.Messages.NotFound));
            return Task.FromResult(response);
        }

        public Task<IMethodResponse<AppUser>> CreateAsync(AppUser entity, Guid actor = default)
        {
            Created = entity;
            return Task.FromResult<IMethodResponse<AppUser>>(MethodResponse<AppUser>.Success(entity));
        }

        public Task<IMethodResponse<AppUser>> UpdateAsync(Guid id, AppUser entity, string? expectedConcurrencyToken = null, Guid actor = default)
        {
            Updated = entity;
            ExpectedConcurrencyToken = expectedConcurrencyToken;
            return Task.FromResult<IMethodResponse<AppUser>>(MethodResponse<AppUser>.Success(entity));
        }

        public Task<IMethodResponse<bool>> RemoveAsync(Guid id, string? expectedConcurrencyToken = null, Guid actor = default)
        {
            RemovedId = id;
            ExpectedConcurrencyToken = expectedConcurrencyToken;
            return Task.FromResult<IMethodResponse<bool>>(MethodResponse<bool>.Success(true));
        }

        public Task<IMethodResponse<IEnumerable<AppUser>>> GetAllAsync(Guid actor = default) => throw new NotSupportedException();

        public Task<IMethodResponse<IEnumerable<AppUser>>> GetAllByPageAsync(int pageNr, int pageSize, Guid actor = default) => throw new NotSupportedException();

        public Task<IMethodResponse<int>> GetCountAsync(Guid actor = default) => throw new NotSupportedException();

        public Task<IMethodResponse<bool>> ExistsAsync(Guid id, Guid actor = default) => throw new NotSupportedException();
    }
}
