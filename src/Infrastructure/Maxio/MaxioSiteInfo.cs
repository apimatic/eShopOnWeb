using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>Reads (once, on success) which invoicing architecture the connected Maxio site uses.</summary>
public class MaxioSiteInfo
{
    private readonly MaxioAdvancedBillingClient _client;
    private CollectionMethod? _method;

    public MaxioSiteInfo(MaxioAdvancedBillingClient client) => _client = client;

    /// <summary>"remittance" on Relationship Invoicing sites, "invoice" on legacy Statements sites.</summary>
    public async Task<CollectionMethod> GetCollectionMethodAsync(CancellationToken ct)
    {
        if (_method is not null) return _method;
        var site = (await _client.Sites.ReadSite(cancellationToken: ct)).Site;
        return _method = site?.RelationshipInvoicingEnabled == true ? CollectionMethod.Remittance : CollectionMethod.Invoice;
    }
}
