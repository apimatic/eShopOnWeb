using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A non-withdrawn bill already raised for a given order — used to stop a second bill being raised for an
/// order that already has a live one.
/// </summary>
public class ActiveInvoiceForOrderSpecification : Specification<Invoice>
{
    public ActiveInvoiceForOrderSpecification(int orderId)
    {
        Query.Where(invoice => invoice.OrderId == orderId && invoice.Status != InvoiceStatus.Withdrawn);
    }
}
