using System.Collections.Concurrent;
using System.Threading;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// In-process, per-key async mutexes used to serialize idempotent operations that are
/// keyed by an entity (for example "one subscription per user + plan"). Cross-instance
/// races are still safe because Maxio enforces a unique subscription reference.
/// </summary>
internal static class KeyedGates
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new();

    public static SemaphoreSlim GetOrAdd(string key, System.Func<string, SemaphoreSlim> valueFactory) =>
        Gates.GetOrAdd(key, valueFactory);
}
