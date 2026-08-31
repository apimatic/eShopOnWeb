using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Notifications for a contact number that the provider is still holding for
/// future delivery — these must be called off when the number is removed.
/// </summary>
public class ScheduledNotificationsForContactSpec : Specification<OrderNotification>
{
    public ScheduledNotificationsForContactSpec(int contactNumberId)
    {
        Query.Where(n => n.ContactNumberId == contactNumberId && n.Status == NotificationStatuses.Scheduled);
    }
}
