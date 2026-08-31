using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class InvoicesByBuyerSpecification : Specification<Invoice>
{
    public InvoicesByBuyerSpecification(string buyerId)
    {
        Query
            .Where(invoice => invoice.BuyerId == buyerId)
            .OrderByDescending(invoice => invoice.CreatedAt);
    }
}
