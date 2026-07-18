using OpenFlowMiner.Api.ApiKeys;

namespace OpenFlowMiner.Api.Tests;

public class InMemoryApiKeyStoreTests
{
    private sealed class TestClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        public void Advance(TimeSpan by) => _now += by;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    [Fact]
    public async Task Create_ReturnsPrefixedOpaqueKey()
    {
        var store = new InMemoryApiKeyStore();
        var key = await store.CreateAsync();

        Assert.StartsWith("ofm_", key.Key);
        Assert.True(key.Key.Length > 20);
        Assert.True(await store.IsValidAsync(key.Key));
    }

    [Fact]
    public async Task Create_ProducesUniqueKeys()
    {
        var store = new InMemoryApiKeyStore();
        var a = await store.CreateAsync();
        var b = await store.CreateAsync();
        Assert.NotEqual(a.Key, b.Key);
    }

    [Fact]
    public async Task TryUse_ValidatesAndCountsUsage()
    {
        var store = new InMemoryApiKeyStore();
        var key = await store.CreateAsync();

        Assert.True(store.TryUse(key.Key));
        Assert.True(store.TryUse(key.Key));
        Assert.False(store.TryUse("ofm_not_real"));
        Assert.False(store.TryUse(""));
    }

    [Fact]
    public async Task Ttl_ExpiresAndInvalidatesKey()
    {
        var clock = new TestClock(DateTimeOffset.UnixEpoch);
        var store = new InMemoryApiKeyStore(clock);
        var key = await store.CreateAsync(TimeSpan.FromDays(1));

        Assert.True(await store.IsValidAsync(key.Key));

        clock.Advance(TimeSpan.FromDays(2));

        Assert.False(await store.IsValidAsync(key.Key));
        Assert.False(store.TryUse(key.Key));
    }

    [Fact]
    public async Task NullTtl_NeverExpires()
    {
        var clock = new TestClock(DateTimeOffset.UnixEpoch);
        var store = new InMemoryApiKeyStore(clock);
        var key = await store.CreateAsync(ttl: null);

        clock.Advance(TimeSpan.FromDays(3650));

        Assert.True(await store.IsValidAsync(key.Key));
    }
}
