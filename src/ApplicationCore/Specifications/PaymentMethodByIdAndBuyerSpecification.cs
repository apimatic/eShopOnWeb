using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Fetches a single saved card by id, scoped to its owner. Scoping by buyer here guarantees one
/// shopper can never read, use, or delete another shopper's saved card.
/// </summary>
public class PaymentMethodByIdAndBuyerSpecification : Specification<SavedPaymentMethod>
{
    public PaymentMethodByIdAndBuyerSpecification(int paymentMethodId, string buyerId)
    {
        Query.Where(pm => pm.Id == paymentMethodId && pm.BuyerId == buyerId);
    }
}
