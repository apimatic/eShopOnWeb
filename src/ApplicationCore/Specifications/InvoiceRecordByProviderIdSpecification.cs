using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class InvoiceRecordByProviderIdSpecification : Specification<InvoiceRecord>, ISingleResultSpecification<InvoiceRecord>
{
    public InvoiceRecordByProviderIdSpecification(string providerInvoiceId)
    {
        Query.Where(r => r.ProviderInvoiceId == providerInvoiceId);
    }
}
