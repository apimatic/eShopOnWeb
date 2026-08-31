using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class InvoiceByOrderSpecification : Specification<Invoice>
{
    public InvoiceByOrderSpecification(int orderId)
    {
        Query.Where(invoice => invoice.OrderId == orderId);
    }
}
