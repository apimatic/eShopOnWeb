using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Finds any bill already raised against a given order (used to prevent double-billing).</summary>
public class InvoiceByOrderIdSpecification : Specification<Invoice>, ISingleResultSpecification<Invoice>
{
    public InvoiceByOrderIdSpecification(int orderId)
    {
        Query.Where(i => i.OrderId == orderId);
    }
}
