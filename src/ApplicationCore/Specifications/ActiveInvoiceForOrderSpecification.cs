using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Bills raised against a given order that have not been withdrawn. Used to stop an order being
/// billed twice while a live bill already stands against it.
/// </summary>
public class ActiveInvoiceForOrderSpecification : Specification<Invoice>, ISingleResultSpecification<Invoice>
{
    public ActiveInvoiceForOrderSpecification(int orderId)
    {
        Query.Where(invoice => invoice.OrderId == orderId && invoice.Status != InvoiceStatus.Canceled);
    }
}
