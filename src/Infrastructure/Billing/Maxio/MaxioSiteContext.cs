using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Caches the site-level facts the integration needs (currency, billing architecture) so the app
/// adapts to whichever Maxio site it is pointed at instead of hard-coding them. Site settings
/// change rarely, so a single successful read is kept for the lifetime of the process; failures
/// are never cached, and the next call retries.
/// </summary>
internal sealed class MaxioSiteContext
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<MaxioOptions> _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile MaxioSite? _site;

    public MaxioSiteContext(IServiceScopeFactory scopeFactory, IOptionsMonitor<MaxioOptions> options)
    {
        _scopeFactory = scopeFactory;
        _options = options;
    }

    public async Task<MaxioSite> GetAsync(CancellationToken cancellationToken)
    {
        if (_site is not null) return _site;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_site is not null) return _site;

            using var scope = _scopeFactory.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<MaxioApiClient>();
            _site = await client.GetSiteAsync(cancellationToken);
            return _site;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>ISO 4217 currency the site prices in.</summary>
    public async Task<string> GetCurrencyAsync(CancellationToken cancellationToken)
    {
        var site = await GetAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(site.Currency) ? "USD" : site.Currency!;
    }

    /// <summary>
    /// The collection method to use for subscriptions created by this app. Both options bill by
    /// invoice rather than charging a card, which is what lets a shopper subscribe without card
    /// capture; the correct spelling depends on the site's billing architecture. An explicit
    /// Maxio:PaymentCollectionMethod setting always wins.
    /// </summary>
    public async Task<string> GetPaymentCollectionMethodAsync(CancellationToken cancellationToken)
    {
        var configured = _options.CurrentValue.PaymentCollectionMethod;
        if (!string.IsNullOrWhiteSpace(configured)) return configured!.Trim();

        var site = await GetAsync(cancellationToken);
        return site.RelationshipInvoicingEnabled ? "remittance" : "invoice";
    }
}
