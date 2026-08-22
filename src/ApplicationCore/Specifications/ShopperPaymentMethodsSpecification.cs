using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ShopperPaymentMethodsSpecification : Specification<ShopperPaymentMethod>
{
    public ShopperPaymentMethodsSpecification(string buyerId)
    {
        Query.Where(m => m.BuyerId == buyerId)
            .OrderByDescending(m => m.Id);
    }
}
