using Application;
using Base.DTO;

namespace Tests;

public class ProcessedMessageServiceTests
{
    [Test]
    public async Task RemoveExpiredAsync_RemovesRowsOlderThanSevenDaysAndSaves()
    {
        var repository = new AppUserServiceTests.FakeProcessedMessageRepository { RemoveCount = 3 };
        var uow = new AppUserServiceTests.FakeUow();
        var service = new ProcessedMessageService(repository, uow);

        var result = await service.RemoveExpiredAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Successful, Is.True);
            Assert.That(result.Value, Is.EqualTo(3));
            Assert.That(repository.RemovedBefore, Is.EqualTo(DateTimeOffset.UtcNow.AddDays(-7)).Within(TimeSpan.FromMinutes(1)));
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
    public async Task RemoveExpiredAsync_SaveRejected_ReturnsSaveError()
    {
        var uow = new AppUserServiceTests.FakeUow
        {
            SaveResult = MethodResponse<int>.Failure(new Error(ErrorDefaults.Codes.SaveFailed, ErrorDefaults.Messages.SaveFailed))
        };
        var service = new ProcessedMessageService(new AppUserServiceTests.FakeProcessedMessageRepository { RemoveCount = 1 }, uow);

        var result = await service.RemoveExpiredAsync();

        Assert.That(result.Error?.Code, Is.EqualTo(ErrorDefaults.Codes.SaveFailed));
    }
}
