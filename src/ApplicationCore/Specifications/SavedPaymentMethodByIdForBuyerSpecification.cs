using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A single saved card by id that also belongs to the given buyer. Shopper-scoped, so one shopper
/// can never use or delete another's card (a wrong buyer yields no match = 404).
/// </summary>
public class SavedPaymentMethodByIdForBuyerSpecification : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodByIdForBuyerSpecification(int paymentMethodId, string buyerId)
    {
        Query.Where(pm => pm.Id == paymentMethodId && pm.BuyerId == buyerId);
    }
}
