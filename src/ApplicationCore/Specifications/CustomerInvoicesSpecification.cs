using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All bills belonging to a single shopper, newest first.</summary>
public class CustomerInvoicesSpecification : Specification<Invoice>
{
    public CustomerInvoicesSpecification(string buyerId)
    {
        Query
            .Where(invoice => invoice.BuyerId == buyerId)
            .OrderByDescending(invoice => invoice.CreatedDate);
    }
}
