using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All of a single shopper's saved cards, newest first.</summary>
public class SavedPaymentMethodsByBuyerSpecification : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodsByBuyerSpecification(string buyerId)
    {
        Query.Where(pm => pm.BuyerId == buyerId)
            .OrderByDescending(pm => pm.CreatedDate);
    }
}
