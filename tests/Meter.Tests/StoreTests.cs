using Meter.Api;
using Xunit;

namespace Meter.Tests;

public sealed class StoreTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "meter-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string file;
    private readonly ReadingStore store;

    public StoreTests()
    {
        Directory.CreateDirectory(folder);
        file = Path.Combine(folder, "readings.db");
        store = new ReadingStore(file);
    }

    private static ReadingRequest Request(long value = 100) =>
        new(Guid.NewGuid().ToString("D"), "MTR-001", value);

    [Fact] public void SubmitAndReplayCreateOneEvent()
    {
        var request = Request();
        Assert.False(store.Submit(request).Replay);
        Assert.True(store.Submit(request).Replay);
        Assert.Single(store.Events());
        Assert.Equal(100, store.Latest("MTR-001")!.ValueWh);
    }

    [Fact] public void ChangedReplayAndDecreasingValuesConflict()
    {
        var request = Request();
        store.Submit(request);
        Assert.Equal(409, Assert.Throws<Rejected>(() => store.Submit(request with { ValueWh = 101 })).Status);
        Assert.Equal(409, Assert.Throws<Rejected>(() => store.Submit(Request(99))).Status);
        Assert.Single(store.Events());
    }

    [Theory] [InlineData(-1L)] [InlineData(1_000_000_000_001L)]
    public void RejectInvalidValues(long value)
    {
        Assert.Equal(400, Assert.Throws<Rejected>(() => store.Submit(Request(value))).Status);
        Assert.Empty(store.Events());
    }

    [Theory] [InlineData(0L)] [InlineData(1_000_000_000_000L)]
    public void AcceptInclusiveValueBoundaries(long value) => Assert.Equal(value, store.Submit(Request(value)).Reading.ValueWh);

    [Fact] public void RejectUnknownMeterAndInvalidIdentifiers()
    {
        Assert.Equal(404, Assert.Throws<Rejected>(() => store.Submit(Request() with { MeterId = "MTR-999" })).Status);
        Assert.Equal(400, Assert.Throws<Rejected>(() => store.Submit(Request() with { MeterId = "x' OR 1=1--" })).Status);
        Assert.Equal(400, Assert.Throws<Rejected>(() => store.Submit(Request() with { OperationId = "invalid" })).Status);
        Assert.Empty(store.Events());
    }

    [Fact] public void RollbackIncludesReadingMeterAndOutbox()
    {
        var request = Request();
        var broken = new ReadingStore(file, () => throw new IOException("injected"));
        Assert.Throws<IOException>(() => broken.Submit(request));
        Assert.Null(store.Latest("MTR-001"));
        Assert.Empty(store.Events());
        Assert.False(store.Submit(request with { ValueWh = 1 }).Replay);
    }

    [Fact] public async Task ConcurrentRetriesConverge()
    {
        var request = Request();
        var tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(() => store.Submit(request))).ToArray();
        var results = await Task.WhenAll(tasks);
        Assert.Single(results, result => !result.Replay);
        Assert.Equal(7, results.Count(result => result.Replay));
        Assert.Single(store.Events());
    }

    [Fact] public void ReopenPreservesResults()
    {
        store.Submit(Request());
        var reopened = new ReadingStore(file);
        Assert.Equal(100, reopened.Latest("MTR-001")!.ValueWh);
        Assert.Single(reopened.Events());
    }

    [Fact] public void RejectsAmbiguousCredentials()
    {
        Assert.Throws<InvalidOperationException>(() => new ApiKeys("short", "short"));
        Assert.Throws<InvalidOperationException>(() => new ApiKeys(new string('a', 32), new string('a', 32)));
    }

    public void Dispose() => Directory.Delete(folder, recursive: true);
}
