using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Resolves site-level Maxio settings that affect how eShopOnWeb must call the API. Registered as
/// a singleton and caches its answer for the life of the process: a site's billing architecture
/// does not change at runtime.
/// </summary>
internal sealed class MaxioSiteCapabilities
{
    private readonly MaxioApiClient _client;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _cardlessPaymentCollectionMethod;

    public MaxioSiteCapabilities(MaxioApiClient client)
    {
        _client = client;
    }

    /// <summary>
    /// The <c>payment_collection_method</c> to use when creating a subscription with no payment
    /// profile attached. Maxio's "automatic" default (the only mode compatible with legacy sites
    /// lacking Relationship Invoicing) tries to charge a card immediately and fails with 422 when
    /// there is none on file, even for a plan configured with "payment method not required". Sites
    /// with Relationship Invoicing enabled must instead use "remittance"; legacy sites use "invoice".
    /// </summary>
    public async Task<string> GetCardlessPaymentCollectionMethodAsync(CancellationToken cancellationToken)
    {
        if (_cardlessPaymentCollectionMethod is not null)
        {
            return _cardlessPaymentCollectionMethod;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_cardlessPaymentCollectionMethod is null)
            {
                var site = await _client.GetAsync<SiteEnvelope>("site.json", cancellationToken);
                _cardlessPaymentCollectionMethod = site?.Site.RelationshipInvoicingEnabled == true
                    ? "remittance"
                    : "invoice";
            }

            return _cardlessPaymentCollectionMethod;
        }
        finally
        {
            _gate.Release();
        }
    }
}
