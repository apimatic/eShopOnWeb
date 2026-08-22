using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ShopperPaymentMethodsSpecification : Specification<ShopperPaymentMethod>
{
    public ShopperPaymentMethodsSpecification(string buyerId)
    {
        Query.Where(m => m.BuyerId == buyerId);
    }

    public ShopperPaymentMethodsSpecification(string buyerId, int paymentMethodId)
    {
        Query.Where(m => m.BuyerId == buyerId && m.Id == paymentMethodId);
    }
}
