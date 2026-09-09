using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A single saved card, scoped to its owner so a shopper can only ever load, use or delete their
/// own — never another shopper's.
/// </summary>
public class PaymentMethodByIdForOwnerSpecification : Specification<PaymentMethod>, ISingleResultSpecification<PaymentMethod>
{
    public PaymentMethodByIdForOwnerSpecification(int paymentMethodId, string ownerId)
    {
        Query.Where(pm => pm.Id == paymentMethodId && pm.OwnerId == ownerId);
    }
}
