using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Every bill raised against a given order.</summary>
public class InvoicesByOrderSpecification : Specification<Invoice>
{
    public InvoicesByOrderSpecification(int orderId)
    {
        Query.Where(i => i.OrderId == orderId);
    }
}
