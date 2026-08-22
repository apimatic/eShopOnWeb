using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedPaymentMethodsByBuyerSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodsByBuyerSpec(string buyerId)
    {
        Query.Where(m => m.BuyerId == buyerId);
    }
}

public class SavedPaymentMethodByIdSpec : Specification<SavedPaymentMethod>, ISingleResultSpecification<SavedPaymentMethod>
{
    public SavedPaymentMethodByIdSpec(int paymentMethodId)
    {
        Query.Where(m => m.Id == paymentMethodId);
    }
}

public class SavedPaymentMethodByBuyerAndIdSpec : Specification<SavedPaymentMethod>, ISingleResultSpecification<SavedPaymentMethod>
{
    public SavedPaymentMethodByBuyerAndIdSpec(string buyerId, int paymentMethodId)
    {
        Query.Where(m => m.BuyerId == buyerId && m.Id == paymentMethodId);
    }
}
