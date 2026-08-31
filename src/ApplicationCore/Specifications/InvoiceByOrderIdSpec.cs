using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class InvoiceByOrderIdSpec : Specification<Invoice>
{
    public InvoiceByOrderIdSpec(int orderId)
    {
        Query.Where(i => i.OrderId == orderId);
    }
}
