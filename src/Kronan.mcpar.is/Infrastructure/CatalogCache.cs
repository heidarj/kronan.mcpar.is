using Microsoft.Extensions.Caching.Memory;

namespace Kronan.McparIs.Infrastructure;

public sealed class CatalogCache(IMemoryCache cache)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task<T?> GetAsync<T>(string key, TimeSpan lifetime, Func<CancellationToken, Task<T?>> fetch, CancellationToken ct)
    {
        if (cache.TryGetValue(key, out T? hit)) return hit;
        await gate.WaitAsync(ct);
        try
        {
            if (cache.TryGetValue(key, out hit)) return hit;
            var value = await fetch(ct);
            if (value is not null) cache.Set(key, value, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = lifetime, Size = 1 });
            return value;
        }
        finally { gate.Release(); }
    }
}
