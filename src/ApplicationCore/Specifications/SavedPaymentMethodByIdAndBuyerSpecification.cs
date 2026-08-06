using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A single saved card, scoped to its owner. Matching on both id and buyer means one shopper can
/// never load, use, or delete another shopper's card — a wrong owner simply yields no result.
/// </summary>
public class SavedPaymentMethodByIdAndBuyerSpecification : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodByIdAndBuyerSpecification(int paymentMethodId, string buyerId)
    {
        Query.Where(pm => pm.Id == paymentMethodId && pm.BuyerId == buyerId);
    }
}
