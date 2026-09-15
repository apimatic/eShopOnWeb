using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All saved cards belonging to a single shopper, newest first.</summary>
public class SavedPaymentMethodsByBuyerSpecification : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodsByBuyerSpecification(string buyerId)
    {
        Query
            .Where(pm => pm.BuyerId == buyerId)
            .OrderByDescending(pm => pm.CreatedAt);
    }
}
