using OpenFlowMiner.Core.Domain;
using OpenFlowMiner.Core.Storage;

namespace OpenFlowMiner.Core.Tests;

public class InMemoryEventLogStoreTests
{
    private sealed class TestClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        public void Advance(TimeSpan by) => _now += by;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private static EventLog Log(string id) => EventLog.FromEvents(id, [], LogProvenance.Unknown);

    [Fact]
    public async Task Save_Then_Get_RoundTrips()
    {
        var store = new InMemoryEventLogStore();
        var id = await store.SaveAsync(Log("a"));

        Assert.Equal("a", id);
        Assert.NotNull(await store.GetAsync("a"));
        Assert.True(await store.ExistsAsync("a"));
    }

    [Fact]
    public async Task Get_UnknownId_ReturnsNull()
        => Assert.Null(await new InMemoryEventLogStore().GetAsync("nope"));

    [Fact]
    public async Task NullTtl_NeverExpires()
    {
        var clock = new TestClock(DateTimeOffset.UnixEpoch);
        var store = new InMemoryEventLogStore(clock);
        await store.SaveAsync(Log("pinned"), ttl: null);

        clock.Advance(TimeSpan.FromDays(3650));

        Assert.NotNull(await store.GetAsync("pinned"));
    }

    [Fact]
    public async Task TtlExpiry_HidesAndRemovesEntry()
    {
        var clock = new TestClock(DateTimeOffset.UnixEpoch);
        var store = new InMemoryEventLogStore(clock);
        await store.SaveAsync(Log("temp"), ttl: TimeSpan.FromHours(1));

        Assert.NotNull(await store.GetAsync("temp"));

        clock.Advance(TimeSpan.FromHours(2));

        Assert.Null(await store.GetAsync("temp"));
        Assert.False(await store.ExistsAsync("temp"));
    }

    [Fact]
    public async Task RemoveExpired_ReclaimsOnlyExpiredEntries()
    {
        var clock = new TestClock(DateTimeOffset.UnixEpoch);
        var store = new InMemoryEventLogStore(clock);
        await store.SaveAsync(Log("keep"), ttl: null);
        await store.SaveAsync(Log("drop"), ttl: TimeSpan.FromHours(1));

        clock.Advance(TimeSpan.FromHours(2));
        var removed = store.RemoveExpired();

        Assert.Equal(1, removed);
        Assert.Equal(1, store.Count);
        Assert.True(await store.ExistsAsync("keep"));
    }
}
