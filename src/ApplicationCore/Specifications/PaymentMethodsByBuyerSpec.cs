using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PaymentMethodsByBuyerSpec : Specification<PaymentMethod>
{
    public PaymentMethodsByBuyerSpec(string buyerId)
    {
        Query.Where(pm => pm.BuyerId == buyerId);
    }
}
