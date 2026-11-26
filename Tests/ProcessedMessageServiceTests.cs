using Application;
using Base.DTO;

namespace Tests;

public class ProcessedMessageServiceTests
{
    private static AppUserServiceTests.FakeProcessedMessageRepository RepositoryWithExpired(int count)
    {
        var repository = new AppUserServiceTests.FakeProcessedMessageRepository();
        repository.Expired.AddRange(Enumerable.Range(0, count).Select(_ => Guid.NewGuid()));
        return repository;
    }

    [Test]
    public async Task RemoveExpiredAsync_RemovesRowsOlderThanSevenDaysAndSaves()
    {
        var repository = RepositoryWithExpired(3);
        var uow = new AppUserServiceTests.FakeUow();
        var service = new ProcessedMessageService(repository, uow);

        var result = await service.RemoveExpiredAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Successful, Is.True);
            Assert.That(result.Value, Is.EqualTo(3));
            Assert.That(repository.Cutoff, Is.EqualTo(DateTimeOffset.UtcNow.AddDays(-7)).Within(TimeSpan.FromMinutes(1)));
            Assert.That(repository.Expired, Is.Empty);
            Assert.That(uow.SaveCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task RemoveExpiredAsync_MoreThanOneBatch_RemovesAndSavesPerBatch()
    {
        var repository = RepositoryWithExpired(ProcessedMessageService.BatchSize * 2 + 500);
        var uow = new AppUserServiceTests.FakeUow();
        var service = new ProcessedMessageService(repository, uow);

        var result = await service.RemoveExpiredAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Value, Is.EqualTo(ProcessedMessageService.BatchSize * 2 + 500));
            Assert.That(repository.RemovedBatchSizes, Is.EqualTo(new[] { ProcessedMessageService.BatchSize, ProcessedMessageService.BatchSize, 500 }));
            Assert.That(uow.SaveCalls, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task RemoveExpiredAsync_ExactlyOneBatch_ChecksForMoreWithoutSavingAgain()
    {
        var repository = RepositoryWithExpired(ProcessedMessageService.BatchSize);
        var uow = new AppUserServiceTests.FakeUow();
        var service = new ProcessedMessageService(repository, uow);

        var result = await service.RemoveExpiredAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Value, Is.EqualTo(ProcessedMessageService.BatchSize));
            Assert.That(uow.SaveCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task RemoveExpiredAsync_NothingExpired_DoesNotSave()
    {
        var uow = new AppUserServiceTests.FakeUow();
        var service = new ProcessedMessageService(new AppUserServiceTests.FakeProcessedMessageRepository(), uow);

        var result = await service.RemoveExpiredAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Successful, Is.True);
            Assert.That(result.Value, Is.Zero);
            Assert.That(uow.SaveCalls, Is.Zero);
        });
    }

    [Test]
    public async Task RemoveExpiredAsync_SaveRejected_ReturnsSaveErrorAndStops()
    {
        var uow = new AppUserServiceTests.FakeUow
        {
            SaveResult = MethodResponse<int>.Failure(new Error(ErrorDefaults.Codes.SaveFailed, ErrorDefaults.Messages.SaveFailed))
        };
        var service = new ProcessedMessageService(RepositoryWithExpired(ProcessedMessageService.BatchSize * 2), uow);

        var result = await service.RemoveExpiredAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Error?.Code, Is.EqualTo(ErrorDefaults.Codes.SaveFailed));
            Assert.That(uow.SaveCalls, Is.EqualTo(1));
        });
    }
}
