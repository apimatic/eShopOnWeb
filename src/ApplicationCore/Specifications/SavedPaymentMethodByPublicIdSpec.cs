using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A single saved card by its public id, scoped to the owning shopper — so one shopper can never load,
/// use or delete another's card.
/// </summary>
public class SavedPaymentMethodByPublicIdSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodByPublicIdSpec(string buyerId, string publicId)
    {
        Query.Where(p => p.BuyerId == buyerId && p.PublicId == publicId);
    }
}
