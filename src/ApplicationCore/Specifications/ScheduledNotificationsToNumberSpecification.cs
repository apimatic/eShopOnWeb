using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A shopper's not-yet-sent scheduled messages addressed to a given number — called off when that
/// number is removed, so nothing is ever sent to it again.
/// </summary>
public class ScheduledNotificationsToNumberSpecification : Specification<OrderNotification>
{
    public ScheduledNotificationsToNumberSpecification(string buyerId, string toNumber)
    {
        Query.Where(n => n.BuyerId == buyerId
            && n.ToNumber == toNumber
            && n.IsScheduled
            && n.ProviderMessageSid != null
            && n.Status == "scheduled");
    }
}
