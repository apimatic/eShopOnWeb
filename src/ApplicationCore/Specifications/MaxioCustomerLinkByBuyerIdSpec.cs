using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class MaxioCustomerLinkByBuyerIdSpec : Specification<MaxioCustomerLink>
{
    public MaxioCustomerLinkByBuyerIdSpec(string buyerId)
    {
        Query.Where(link => link.BuyerId == buyerId);
    }
}
