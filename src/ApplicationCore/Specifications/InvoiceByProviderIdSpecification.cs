using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class InvoiceByProviderIdSpecification : Specification<Invoice>
{
    public InvoiceByProviderIdSpecification(string providerInvoiceId)
    {
        Query.Where(invoice => invoice.ProviderInvoiceId == providerInvoiceId);
    }
}
