using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedPaymentMethodsByBuyerIdSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodsByBuyerIdSpec(string buyerId)
    {
        Query.Where(p => p.BuyerId == buyerId)
            .OrderByDescending(p => p.CreatedAt);
    }
}

public class SavedPaymentMethodByIdSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodByIdSpec(int paymentMethodId)
    {
        Query.Where(p => p.Id == paymentMethodId);
    }
}
