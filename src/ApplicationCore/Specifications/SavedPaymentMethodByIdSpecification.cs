using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A single saved card scoped to its owner — the buyerId guard ensures one shopper can never load,
/// use, or delete another shopper's saved card.
/// </summary>
public sealed class SavedPaymentMethodByIdSpecification : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodByIdSpecification(int id, string buyerId)
    {
        Query.Where(m => m.Id == id && m.BuyerId == buyerId);
    }
}
