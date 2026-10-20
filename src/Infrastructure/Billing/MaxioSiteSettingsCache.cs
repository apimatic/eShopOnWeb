using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Caches site-level Maxio settings for the life of the process; they change only by merchant reconfiguration.
/// Registered as a singleton; only successful reads are cached, so a failed read is retried by the next request.
/// </summary>
public class MaxioSiteSettingsCache
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool? _relationshipInvoicingEnabled;

    public async Task<bool> GetRelationshipInvoicingEnabledAsync(Func<CancellationToken, Task<bool>> load, CancellationToken cancellationToken)
    {
        if (_relationshipInvoicingEnabled is bool cached)
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _relationshipInvoicingEnabled ??= await load(cancellationToken);
            return _relationshipInvoicingEnabled.Value;
        }
        finally
        {
            _gate.Release();
        }
    }
}
