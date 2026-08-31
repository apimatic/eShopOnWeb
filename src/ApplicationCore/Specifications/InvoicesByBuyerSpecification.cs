using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class InvoicesByBuyerSpecification : Specification<Invoice>
{
    public InvoicesByBuyerSpecification(string buyerId)
    {
        Query
            .Where(i => i.BuyerId == buyerId)
            .OrderByDescending(i => i.CreatedDate);
    }
}
