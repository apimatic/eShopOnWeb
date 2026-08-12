using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Still-scheduled messages queued with the provider for a given destination number.
/// Used to call off pending follow-ups when a shopper removes that number, so nothing
/// is ever sent to it again.
/// </summary>
public class PendingScheduledNotificationsByNumberSpecification : Specification<OrderNotification>
{
    public PendingScheduledNotificationsByNumberSpecification(string toNumber)
    {
        Query.Where(n =>
            n.ToNumber == toNumber &&
            n.IsScheduled &&
            n.ProviderMessageSid != null &&
            n.DeliveryStatus == MessageDeliveryStatus.Scheduled);
    }
}

/// <summary>
/// eShop's own record of messages actually handed to the provider (have a provider
/// message id) whose creation falls within a range — the eShop side of reconciliation.
/// </summary>
public class SentNotificationsInRangeSpecification : Specification<OrderNotification>
{
    public SentNotificationsInRangeSpecification(System.DateTimeOffset fromUtc, System.DateTimeOffset toUtc)
    {
        Query.Where(n =>
            n.ProviderMessageSid != null &&
            n.CreatedDate >= fromUtc &&
            n.CreatedDate <= toUtc);
    }
}
