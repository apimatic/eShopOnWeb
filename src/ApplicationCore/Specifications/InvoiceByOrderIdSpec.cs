using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All invoices raised against a given order, with their lines.</summary>
public class InvoiceByOrderIdSpec : Specification<Invoice>
{
    public InvoiceByOrderIdSpec(int orderId)
    {
        Query.Where(i => i.OrderId == orderId)
            .Include(i => i.Items);
    }
}
