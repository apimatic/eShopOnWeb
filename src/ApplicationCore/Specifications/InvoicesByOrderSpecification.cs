using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public sealed class InvoicesByOrderSpecification : Specification<Invoice>
{
    public InvoicesByOrderSpecification(int orderId)
    {
        Query.Where(i => i.OrderId == orderId);
    }
}
