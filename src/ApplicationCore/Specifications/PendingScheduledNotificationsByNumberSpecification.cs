using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Scheduled messages for a given owner and destination number that have not yet been sent.
/// Used to call off pending follow-ups when a shopper removes a number from file, so nothing is
/// ever sent to a number that has been deleted.
/// </summary>
public class PendingScheduledNotificationsByNumberSpecification : Specification<OrderNotification>
{
    public PendingScheduledNotificationsByNumberSpecification(string ownerId, string number)
    {
        Query.Where(n => n.OwnerId == ownerId
            && n.ToNumber == number
            && n.IsScheduled
            && n.ProviderMessageSid != null);
    }
}
