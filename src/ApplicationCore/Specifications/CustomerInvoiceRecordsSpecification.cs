using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class CustomerInvoiceRecordsSpecification : Specification<InvoiceRecord>
{
    public CustomerInvoiceRecordsSpecification(string buyerId)
    {
        Query.Where(r => r.BuyerId == buyerId)
            .OrderByDescending(r => r.CreatedAt);
    }
}
