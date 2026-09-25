using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedPaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedPaymentMethodsByBuyerSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodsByBuyerSpec(string buyerId)
    {
        Query.Where(pm => pm.BuyerId == buyerId);
    }
}

/// <summary>A single saved card, scoped to its owner so one shopper can never load another's.</summary>
public class SavedPaymentMethodByIdForBuyerSpec : Specification<SavedPaymentMethod>,
    ISingleResultSpecification<SavedPaymentMethod>
{
    public SavedPaymentMethodByIdForBuyerSpec(int paymentMethodId, string buyerId)
    {
        Query.Where(pm => pm.Id == paymentMethodId && pm.BuyerId == buyerId);
    }
}
