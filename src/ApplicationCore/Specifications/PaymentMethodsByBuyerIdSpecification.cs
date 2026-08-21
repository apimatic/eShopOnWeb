using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PaymentMethodsByBuyerIdSpecification : Specification<PaymentMethod>
{
    public PaymentMethodsByBuyerIdSpecification(string buyerId)
    {
        Query.Where(p => p.BuyerId == buyerId);
    }
}
