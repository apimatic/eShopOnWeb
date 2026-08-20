using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PaymentMethodsByBuyerSpec : Specification<PaymentMethod>
{
    public PaymentMethodsByBuyerSpec(string buyerId)
    {
        Query.Where(paymentMethod => paymentMethod.BuyerId == buyerId)
            .OrderByDescending(paymentMethod => paymentMethod.CreatedDate);
    }
}
