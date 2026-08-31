using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class InvoiceByProviderIdSpec : Specification<Invoice>
{
    public InvoiceByProviderIdSpec(string providerInvoiceId)
    {
        Query.Where(i => i.ProviderInvoiceId == providerInvoiceId);
    }
}
