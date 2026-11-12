using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace Pvm.Api.Features.Banking;

/// <summary>
/// Keeps the banking status in memory for a short time. Each status read asks Acumatica for
/// every cash account, which takes seconds, and the figures change only when a statement is
/// imported. An import calls <see cref="Invalidate"/>, so the next read shows the new statement.
/// </summary>
/// <remarks>
/// A read that started before an invalidation must not put its older result back in the cache.
/// Each cached entry therefore carries the invalidation token that was current when its read
/// started; <see cref="Invalidate"/> cancels that token, which expires the entry at once.
/// </remarks>
public sealed class BankingStatusCache(IMemoryCache cache)
{
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(60);

    private const string CacheKey = "banking:status";
    private readonly object _sync = new();
    private CancellationTokenSource _invalidation = new();

    /// <summary>
    /// The cached status, or a fresh read. A result with a failed account read is not cached,
    /// so a short Acumatica outage does not hide the accounts for a full minute.
    /// </summary>
    public async Task<BankingStatusResponse> GetOrReadAsync(
        Func<CancellationToken, Task<BankingStatusRead>> read,
        CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(CacheKey, out BankingStatusResponse? cached) && cached is not null)
        {
            return cached;
        }

        CancellationToken invalidation;
        lock (_sync)
        {
            invalidation = _invalidation.Token;
        }

        var result = await read(cancellationToken);
        if (result.Complete && !invalidation.IsCancellationRequested)
        {
            cache.Set(
                CacheKey,
                result.Response,
                new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = Duration
                }.AddExpirationToken(new CancellationChangeToken(invalidation)));
        }

        return result.Response;
    }

    /// <summary>Drops the cached status. Call it after anything is imported.</summary>
    public void Invalidate()
    {
        CancellationTokenSource previous;
        lock (_sync)
        {
            previous = _invalidation;
            _invalidation = new CancellationTokenSource();
        }

        // Not disposed: a read in flight may still hold its token and register on it.
        previous.Cancel();
        cache.Remove(CacheKey);
    }
}

/// <summary>
/// One status read. <paramref name="Complete"/> is false when an account could not be read.
/// </summary>
public sealed record BankingStatusRead(BankingStatusResponse Response, bool Complete);
