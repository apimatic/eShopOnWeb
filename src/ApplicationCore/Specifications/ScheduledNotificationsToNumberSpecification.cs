using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Messages still queued with the provider (not yet sent) addressed to a specific
/// buyer's number — the ones that must be called off when the number is removed.
/// </summary>
public class ScheduledNotificationsToNumberSpecification : Specification<OrderNotification>
{
    public ScheduledNotificationsToNumberSpecification(string buyerId, string phoneNumber)
    {
        Query.Where(n => n.BuyerId == buyerId
            && n.ToNumber == phoneNumber
            && n.Status == NotificationStatuses.Scheduled
            && n.MessageSid != null);
    }
}
