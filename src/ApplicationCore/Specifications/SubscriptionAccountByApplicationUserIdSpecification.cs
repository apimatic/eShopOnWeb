using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SubscriptionAccountByApplicationUserIdSpecification : Specification<SubscriptionAccount>, ISingleResultSpecification<SubscriptionAccount>
{
    public SubscriptionAccountByApplicationUserIdSpecification(string applicationUserId)
    {
        Query.Where(account => account.ApplicationUserId == applicationUserId);
    }
}
