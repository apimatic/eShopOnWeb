using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class InvoiceByOrderIdSpecification : Specification<Invoice>
{
    public InvoiceByOrderIdSpecification(int orderId)
    {
        Query.Where(i => i.OrderId == orderId);
    }
}
