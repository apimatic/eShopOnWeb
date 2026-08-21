using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedPaymentMethodsByBuyerSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodsByBuyerSpec(string buyerId)
    {
        Query.Where(p => p.BuyerId == buyerId);
    }
}

public class SavedPaymentMethodByIdForBuyerSpec : Specification<SavedPaymentMethod>, ISingleResultSpecification<SavedPaymentMethod>
{
    public SavedPaymentMethodByIdForBuyerSpec(int paymentMethodId, string buyerId)
    {
        Query.Where(p => p.Id == paymentMethodId && p.BuyerId == buyerId);
    }
}
