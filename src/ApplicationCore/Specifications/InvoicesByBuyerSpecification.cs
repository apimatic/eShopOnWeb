using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>A shopper's own bills, newest first.</summary>
public class InvoicesByBuyerSpecification : Specification<Invoice>
{
    public InvoicesByBuyerSpecification(string buyerId)
    {
        Query
            .Where(invoice => invoice.BuyerId == buyerId)
            .OrderByDescending(invoice => invoice.CreatedAt);
    }
}
