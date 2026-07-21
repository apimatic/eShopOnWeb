using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Memoizes a successful metered-usage-component validation so it happens at most once per process
/// (at startup, and again lazily before the first usage call if the startup attempt failed or was
/// still in flight) rather than on every single usage report. Only success is cached - a failed
/// validation is retried on the next call, since the misconfiguration may since have been fixed.
/// Registered as a singleton; shared across the transient <c>MaxioBillingClient</c> instances that
/// the typed-HttpClient registration creates.
/// </summary>
public sealed class MeteredComponentValidationCache
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile bool _validated;

    public async Task EnsureValidatedAsync(Func<Task> validate)
    {
        if (_validated)
        {
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_validated)
            {
                return;
            }

            await validate().ConfigureAwait(false);
            _validated = true;
        }
        finally
        {
            _gate.Release();
        }
    }
}
