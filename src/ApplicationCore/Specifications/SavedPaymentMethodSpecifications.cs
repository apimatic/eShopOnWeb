using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All cards a shopper has saved.</summary>
public sealed class SavedPaymentMethodsByBuyerSpecification : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodsByBuyerSpecification(string buyerId)
    {
        Query.Where(m => m.BuyerId == buyerId);
    }
}

/// <summary>
/// A single saved card, scoped to its owner — so one shopper can never see, use, or delete another's.
/// </summary>
public sealed class SavedPaymentMethodByIdSpecification : Specification<SavedPaymentMethod>, ISingleResultSpecification<SavedPaymentMethod>
{
    public SavedPaymentMethodByIdSpecification(string buyerId, string paymentMethodId)
    {
        Query.Where(m => m.BuyerId == buyerId && m.PaymentMethodId == paymentMethodId);
    }
}
