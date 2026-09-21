using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ShopperPaymentProfileByBuyerSpecification : Specification<ShopperPaymentProfile>
{
    public ShopperPaymentProfileByBuyerSpecification(string buyerId)
    {
        Query.Where(p => p.BuyerId == buyerId);
    }
}
