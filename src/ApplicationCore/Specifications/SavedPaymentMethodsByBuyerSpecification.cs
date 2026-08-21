using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>A shopper's saved cards. Every saved-card read/write is scoped through this by owner.</summary>
public class SavedPaymentMethodsByBuyerSpecification : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodsByBuyerSpecification(string buyerId)
    {
        Query.Where(pm => pm.BuyerId == buyerId);
    }

    public SavedPaymentMethodsByBuyerSpecification(string buyerId, int paymentMethodId)
    {
        Query.Where(pm => pm.BuyerId == buyerId && pm.Id == paymentMethodId);
    }
}
