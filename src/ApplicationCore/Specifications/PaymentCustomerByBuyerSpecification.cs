using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PaymentCustomerByBuyerSpecification : Specification<PaymentCustomer>
{
    public PaymentCustomerByBuyerSpecification(string buyerId)
    {
        Query.Where(c => c.BuyerId == buyerId);
    }
}
