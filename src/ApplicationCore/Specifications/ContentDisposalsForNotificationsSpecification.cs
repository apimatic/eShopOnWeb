using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>The content-disposal records for a set of notifications.</summary>
public class ContentDisposalsForNotificationsSpecification : Specification<NotificationContentDisposal>
{
    public ContentDisposalsForNotificationsSpecification(IEnumerable<int> notificationIds)
    {
        var ids = notificationIds.ToList();
        Query.Where(d => ids.Contains(d.NotificationId));
    }
}

/// <summary>The content-disposal record for a single notification, if any.</summary>
public class ContentDisposalByNotificationSpecification : Specification<NotificationContentDisposal>
{
    public ContentDisposalByNotificationSpecification(int notificationId)
    {
        Query.Where(d => d.NotificationId == notificationId);
    }
}
