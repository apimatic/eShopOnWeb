using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A single saved card scoped to its owner, so one shopper can never fetch, use, or
/// delete another's card.
/// </summary>
public class SavedPaymentMethodByIdSpecification : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodByIdSpecification(int paymentMethodId, string buyerId)
    {
        Query.Where(pm => pm.Id == paymentMethodId && pm.BuyerId == buyerId);
    }
}
