using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class MaxioCustomerLinkByBuyerIdSpecification : Specification<MaxioCustomerLink>
{
    public MaxioCustomerLinkByBuyerIdSpecification(string buyerId)
    {
        Query.Where(link => link.BuyerId == buyerId);
    }
}
