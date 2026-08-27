using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PaymentMethodByIdSpecification : Specification<PaymentMethod>
{
    public PaymentMethodByIdSpecification(int paymentMethodId)
    {
        Query.Where(p => p.Id == paymentMethodId);
    }
}

public class PaymentMethodsByBuyerSpecification : Specification<PaymentMethod>
{
    public PaymentMethodsByBuyerSpecification(string buyerId)
    {
        Query.Where(p => p.BuyerId == buyerId)
            .OrderByDescending(p => p.CreatedAt);
    }
}
