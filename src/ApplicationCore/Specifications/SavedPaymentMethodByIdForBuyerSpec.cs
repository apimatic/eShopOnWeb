using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Loads a single saved card by id, but only if it belongs to the given shopper — the buyer
/// filter is what stops one shopper using or deleting another's card.
/// </summary>
public class SavedPaymentMethodByIdForBuyerSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodByIdForBuyerSpec(string buyerId, int paymentMethodId)
    {
        Query.Where(pm => pm.Id == paymentMethodId && pm.BuyerId == buyerId);
    }
}
