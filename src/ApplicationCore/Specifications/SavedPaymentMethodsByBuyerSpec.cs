using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All cards saved by a given shopper, newest first.</summary>
public class SavedPaymentMethodsByBuyerSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodsByBuyerSpec(string buyerId)
    {
        Query.Where(p => p.BuyerId == buyerId)
             .OrderByDescending(p => p.CreatedAt);
    }
}
