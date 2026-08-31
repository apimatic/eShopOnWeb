using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class InvoicesByBuyerSpec : Specification<Invoice>
{
    public InvoicesByBuyerSpec(string buyerId)
    {
        Query.Where(i => i.BuyerId == buyerId)
             .OrderByDescending(i => i.CreatedDate);
    }
}
