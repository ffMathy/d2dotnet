using D2Dotnet.Map.Generation;

namespace D2Dotnet.Map.Tests.Generation;

public class LruCacheTests
{
    [Fact]
    public void KeepsAtMostTwoGenerations()
    {
        var cache = new LruCache<string>(2);
        cache.Set("a", "A");
        cache.Set("b", "B");
        Assert.Equal("A", cache.Get("a"));

        // generation full: a,b become old
        cache.Set("c", "C");
        Assert.Equal("B", cache.Get("b")); // promoted to the current generation
        Assert.Equal("C", cache.Get("c"));

        // current generation (c, b) full again: old generation (a) is dropped
        cache.Set("d", "D");
        Assert.Null(cache.Get("a"));
        Assert.Equal("B", cache.Get("b"));
        Assert.Equal("C", cache.Get("c"));
        Assert.Equal("D", cache.Get("d"));
        Assert.Null(cache.Get("missing"));
    }

    [Fact]
    public void OverwritesExistingKeys()
    {
        var cache = new LruCache<string>(10);
        cache.Set("a", "1");
        cache.Set("a", "2");
        Assert.Equal("2", cache.Get("a"));
    }
}
