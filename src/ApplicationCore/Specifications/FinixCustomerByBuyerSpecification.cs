using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class FinixCustomerByBuyerSpecification : Specification<FinixCustomer>
{
    public FinixCustomerByBuyerSpecification(string buyerId)
    {
        Query.Where(customer => customer.BuyerId == buyerId);
    }
}
