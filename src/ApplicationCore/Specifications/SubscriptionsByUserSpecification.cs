using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Selects the subscriptions belonging to one eShopOnWeb user (mirrors
/// <see cref="CustomerOrdersSpecification"/>). The billing provider is the system of record, so this
/// filters the subscriptions the billing client returned rather than a database table.
/// </summary>
public class SubscriptionsByUserSpecification : Specification<Subscription>
{
    public SubscriptionsByUserSpecification(string userReference)
    {
        Query.Where(s => s.UserReference == userReference);
    }
}
