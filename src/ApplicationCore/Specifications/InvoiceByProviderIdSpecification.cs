using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>The single bill with the given provider identifier.</summary>
public class InvoiceByProviderIdSpecification : Specification<Invoice>
{
    public InvoiceByProviderIdSpecification(string providerInvoiceId)
    {
        Query.Where(i => i.ProviderInvoiceId == providerInvoiceId);
    }
}
