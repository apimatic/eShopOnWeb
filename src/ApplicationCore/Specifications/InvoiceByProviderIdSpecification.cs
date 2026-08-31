using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Finds the local record of a bill by the provider's invoice id (the public invoice id).</summary>
public class InvoiceByProviderIdSpecification : Specification<Invoice>, ISingleResultSpecification<Invoice>
{
    public InvoiceByProviderIdSpecification(string providerInvoiceId)
    {
        Query.Where(i => i.ProviderInvoiceId == providerInvoiceId);
    }
}
