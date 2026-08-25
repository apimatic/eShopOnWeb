using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PaymentMethodsByBuyerIdSpec : Specification<PaymentMethod>
{
    public PaymentMethodsByBuyerIdSpec(string buyerId)
    {
        Query.Where(pm => pm.BuyerId == buyerId);
    }
}
