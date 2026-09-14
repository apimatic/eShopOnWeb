using System;
using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Serializes subscribe attempts for the same subscriber inside this process, so that a
/// double-clicked Subscribe button cannot run two enrollments past each other's
/// "are you already subscribed?" check.
/// </summary>
/// <remarks>
/// Locks are striped over a fixed array rather than allocated per subscriber: bounded memory,
/// no eviction to get wrong, and the only cost is that two unrelated subscribers occasionally
/// queue behind one another for the length of one enrollment.
/// <para>
/// This is a single-process guard. Across instances, correctness comes from Maxio instead:
/// customer and subscription references are unique per site, so a racing duplicate is rejected
/// there and <see cref="MaxioSubscriptionService"/> resolves it to the existing record.
/// </para>
/// </remarks>
public class MaxioSubscriberLocks
{
    private const int StripeCount = 64;

    private readonly SemaphoreSlim[] _stripes;

    public MaxioSubscriberLocks()
    {
        _stripes = new SemaphoreSlim[StripeCount];
        for (var i = 0; i < StripeCount; i++)
        {
            _stripes[i] = new SemaphoreSlim(1, 1);
        }
    }

    public SemaphoreSlim For(string subscriberKey)
    {
        var hash = StringComparer.Ordinal.GetHashCode(subscriberKey);
        var index = (hash & int.MaxValue) % StripeCount;
        return _stripes[index];
    }
}
