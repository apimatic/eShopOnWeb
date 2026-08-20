using System;
using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class SubscriptionOperationLock
{
    private readonly SemaphoreSlim[] _stripes = CreateStripes();

    public SemaphoreSlim For(string key)
    {
        var index = (key.GetHashCode(StringComparison.Ordinal) & int.MaxValue) % _stripes.Length;
        return _stripes[index];
    }

    private static SemaphoreSlim[] CreateStripes()
    {
        var stripes = new SemaphoreSlim[64];
        for (var i = 0; i < stripes.Length; i++) stripes[i] = new SemaphoreSlim(1, 1);
        return stripes;
    }
}
