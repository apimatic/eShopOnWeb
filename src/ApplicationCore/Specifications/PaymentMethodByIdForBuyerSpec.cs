using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PaymentMethodByIdForBuyerSpec : Specification<PaymentMethod>
{
    public PaymentMethodByIdForBuyerSpec(int paymentMethodId, string buyerId)
    {
        Query.Where(p => p.Id == paymentMethodId && p.BuyerId == buyerId);
    }
}
