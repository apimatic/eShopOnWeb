using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public sealed class NotificationsByOwnerSpecification : Specification<Notification>
{
    public NotificationsByOwnerSpecification(string ownerId)
    {
        Query.Where(n => n.OwnerId == ownerId)
            .OrderBy(n => n.Id);
    }
}
