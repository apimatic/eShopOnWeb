using System;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.Extensions.Logging;
using CollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// The payment collection method a subscription without a payment method must use on the configured Maxio site:
/// <c>remittance</c> on Relationship Invoicing sites, <c>invoice</c> on legacy Statements sites. Read from the site
/// itself (<c>Sites.ReadSite</c>), lazily on first use and cached for the process; never a config value or constant.
/// </summary>
public sealed class MaxioSiteArchitecture
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly ILogger<MaxioSiteArchitecture> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CollectionMethod? _cached;

    public MaxioSiteArchitecture(MaxioAdvancedBillingClient client, ILogger<MaxioSiteArchitecture> logger)
    {
        _client = client;
        _logger = logger;
    }

    /// <summary>The invoice-style collection method valid for the site. Throws <see cref="BillingException"/> (Unavailable) if it cannot be determined.</summary>
    public async Task<CollectionMethod> GetInvoiceCollectionMethodAsync(CancellationToken cancellationToken)
    {
        var cached = _cached;
        if (cached is not null)
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_cached is not null)
            {
                return _cached;
            }

            bool? relationshipInvoicing;
            try
            {
                var response = await _client.Sites.ReadSite(cancellationToken: cancellationToken);
                relationshipInvoicing = response.Site?.RelationshipInvoicingEnabled;
            }
            catch (SdkException ex)
            {
                _logger.LogError(ex, "Reading the Maxio site's invoicing architecture failed.");
                throw new BillingException(BillingErrorKind.Unavailable, "The billing service is temporarily unavailable. Please try again.", null, ex);
            }

            if (relationshipInvoicing is null)
            {
                _logger.LogError("The Maxio site did not report relationship_invoicing_enabled; cannot choose a payment collection method.");
                throw new BillingException(BillingErrorKind.Unavailable, "The billing service is temporarily unavailable. Please try again.");
            }

            _cached = relationshipInvoicing.Value ? CollectionMethod.Remittance : CollectionMethod.Invoice;
            _logger.LogInformation(
                "Maxio site uses {Architecture}; subscriptions are created with payment collection method {CollectionMethod}.",
                relationshipInvoicing.Value ? "Relationship Invoicing" : "Statements", _cached.Value);
            return _cached;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Forgets the cached architecture (e.g. after Maxio refused the collection method).</summary>
    public void Invalidate() => _cached = null;
}
