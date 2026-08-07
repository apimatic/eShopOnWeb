using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A single saved card by id, scoped to its owner so one shopper can never read, use, or
/// delete another's card.
/// </summary>
public class SavedPaymentMethodByIdSpecification : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodByIdSpecification(string buyerId, int paymentMethodId)
    {
        Query.Where(pm => pm.Id == paymentMethodId && pm.BuyerId == buyerId);
    }
}
