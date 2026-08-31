using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// All invoices belonging to a single shopper (their bills), newest first, with lines.
/// </summary>
public class CustomerInvoicesSpecification : Specification<Invoice>
{
    public CustomerInvoicesSpecification(string buyerId)
    {
        Query.Where(i => i.BuyerId == buyerId)
            .Include(i => i.Items)
            .OrderByDescending(i => i.CreatedAt);
    }
}
