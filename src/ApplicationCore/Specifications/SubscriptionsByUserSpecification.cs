using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Selects the subscriptions belonging to one eShopOnWeb user, newest period first.
/// Mirrors <see cref="CustomerOrdersSpecification"/>.
/// </summary>
public class SubscriptionsByUserSpecification : Specification<Subscription>
{
    public SubscriptionsByUserSpecification(string buyerId)
    {
        Query.Where(s => s.BuyerId == buyerId)
            .OrderByDescending(s => s.Id);
    }
}
