using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class InvoicesForOrderSpecification : Specification<Invoice>
{
    public InvoicesForOrderSpecification(int orderId)
    {
        Query.Where(invoice => invoice.OrderId == orderId);
    }
}
