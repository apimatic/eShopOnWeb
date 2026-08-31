using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A still-live bill (raised or issued, i.e. not withdrawn) already standing against an order.
/// Used to stop a second bill being raised against the same order.
/// </summary>
public class ActiveInvoiceForOrderSpecification : Specification<Invoice>, ISingleResultSpecification<Invoice>
{
    public ActiveInvoiceForOrderSpecification(int orderId)
    {
        Query.Where(i => i.OrderId == orderId && i.Status != InvoiceStatus.Withdrawn);
    }
}
