using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class LatestSavedPaymentMethodByBuyerSpec : Specification<SavedPaymentMethod>
{
    public LatestSavedPaymentMethodByBuyerSpec(string buyerId)
    {
        Query.Where(m => m.BuyerId == buyerId && m.PayPalCustomerId != null)
            .OrderByDescending(m => m.Id)
            .Take(1);
    }
}
