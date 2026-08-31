using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Finds a single invoice by the provider's identifier for it (the public invoice id),
/// eager-loading its billed lines.
/// </summary>
public class InvoiceByProviderIdSpec : Specification<Invoice>, ISingleResultSpecification<Invoice>
{
    public InvoiceByProviderIdSpec(string providerInvoiceId)
    {
        Query.Where(i => i.ProviderInvoiceId == providerInvoiceId)
            .Include(i => i.Items);
    }
}
