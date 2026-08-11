using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>The caller's own saved cards, newest first.</summary>
public class SavedPaymentMethodsByBuyerSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodsByBuyerSpec(string buyerId)
    {
        Query.Where(pm => pm.BuyerId == buyerId)
             .OrderByDescending(pm => pm.CreatedAt);
    }
}

/// <summary>
/// A single saved card scoped to its owner, so one shopper can never see, use, or delete another's.
/// </summary>
public class SavedPaymentMethodByIdSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodByIdSpec(int paymentMethodId, string buyerId)
    {
        Query.Where(pm => pm.Id == paymentMethodId && pm.BuyerId == buyerId);
    }
}
