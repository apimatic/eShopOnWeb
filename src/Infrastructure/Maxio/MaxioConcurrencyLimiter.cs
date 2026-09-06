using System;
using System.Threading;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Process-wide budget of concurrent Maxio calls. Registered as a singleton so the budget survives
/// the periodic recycling of the HttpClient handler chain.
/// </summary>
public sealed class MaxioConcurrencyLimiter : IDisposable
{
    public MaxioConcurrencyLimiter(IOptions<MaxioSettings> settings)
    {
        var maxConcurrent = Math.Max(1, settings.Value.MaxConcurrentRequests);
        Gate = new SemaphoreSlim(maxConcurrent, maxConcurrent);
    }

    public SemaphoreSlim Gate { get; }

    public void Dispose() => Gate.Dispose();
}
