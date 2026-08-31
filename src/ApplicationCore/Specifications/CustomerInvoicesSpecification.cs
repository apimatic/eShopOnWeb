using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All bills belonging to one shopper, newest first.</summary>
public sealed class CustomerInvoicesSpecification : Specification<Invoice>
{
    public CustomerInvoicesSpecification(string buyerId)
    {
        Query.Where(i => i.BuyerId == buyerId)
            .OrderByDescending(i => i.CreatedDate);
    }
}
