using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A single saved card that belongs to a specific shopper. Scoping by buyer id ensures one shopper
/// can never see, use or delete another's saved card.
/// </summary>
public class SavedPaymentMethodByIdForBuyerSpecification : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodByIdForBuyerSpecification(int paymentMethodId, string buyerId)
    {
        Query.Where(pm => pm.Id == paymentMethodId && pm.BuyerId == buyerId);
    }
}
