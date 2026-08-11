using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All saved cards belonging to a shopper.</summary>
public class SavedPaymentMethodsForBuyerSpecification : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodsForBuyerSpecification(string buyerId)
    {
        Query.Where(pm => pm.BuyerId == buyerId);
    }
}

/// <summary>A single saved card, scoped to its owner so it can't be seen, used, or deleted by others.</summary>
public class SavedPaymentMethodByIdForBuyerSpecification : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodByIdForBuyerSpecification(int paymentMethodId, string buyerId)
    {
        Query.Where(pm => pm.Id == paymentMethodId && pm.BuyerId == buyerId);
    }
}
