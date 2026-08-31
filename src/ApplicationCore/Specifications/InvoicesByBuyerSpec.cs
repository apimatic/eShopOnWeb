using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All of a shopper's bills, most recently raised first.</summary>
public sealed class InvoicesByBuyerSpec : Specification<Invoice>
{
    public InvoicesByBuyerSpec(string buyerId)
    {
        Query.Where(invoice => invoice.BuyerId == buyerId)
             .OrderByDescending(invoice => invoice.CreatedDate);
    }
}
