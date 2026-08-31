using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class InvoiceByProviderIdSpecification : Specification<Invoice>, ISingleResultSpecification<Invoice>
{
    public InvoiceByProviderIdSpecification(string providerInvoiceId)
    {
        Query.Where(i => i.ProviderInvoiceId == providerInvoiceId);
    }
}
