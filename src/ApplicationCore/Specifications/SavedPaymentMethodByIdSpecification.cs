using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A single saved card by id, scoped to the owning shopper so one shopper can never load, use or
/// delete another's card.
/// </summary>
public class SavedPaymentMethodByIdSpecification : Specification<SavedPaymentMethod>, ISingleResultSpecification<SavedPaymentMethod>
{
    public SavedPaymentMethodByIdSpecification(int id, string buyerId)
    {
        Query.Where(pm => pm.Id == id && pm.BuyerId == buyerId);
    }
}
