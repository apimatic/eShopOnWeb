using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>A single saved card scoped to its owner, so one shopper can never reach another's card.</summary>
public class SavedPaymentMethodByIdSpecification : Specification<SavedPaymentMethod>, ISingleResultSpecification<SavedPaymentMethod>
{
    public SavedPaymentMethodByIdSpecification(int id, string buyerId)
    {
        Query.Where(pm => pm.Id == id && pm.BuyerId == buyerId);
    }
}
