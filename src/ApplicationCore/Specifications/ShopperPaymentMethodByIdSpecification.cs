using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ShopperPaymentMethodByIdSpecification : Specification<ShopperPaymentMethod>, ISingleResultSpecification<ShopperPaymentMethod>
{
    public ShopperPaymentMethodByIdSpecification(int paymentMethodId, string buyerId)
    {
        Query.Where(m => m.Id == paymentMethodId && m.BuyerId == buyerId);
    }
}
