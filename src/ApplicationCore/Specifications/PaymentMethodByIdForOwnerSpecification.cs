using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A single saved card by id, constrained to its owner. Because the owner is part of the query, a
/// card belonging to another shopper is simply not found — one shopper can never load another's card.
/// </summary>
public class PaymentMethodByIdForOwnerSpecification : Specification<PaymentMethod>
{
    public PaymentMethodByIdForOwnerSpecification(string ownerId, int paymentMethodId)
    {
        Query.Where(pm => pm.Id == paymentMethodId && pm.OwnerId == ownerId);
    }
}
