using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class BuyerPaymentMethodsSpecification : Specification<PaymentMethod>
{
    public BuyerPaymentMethodsSpecification(string buyerId)
    {
        Query.Where(pm => pm.BuyerId == buyerId);
    }
}
