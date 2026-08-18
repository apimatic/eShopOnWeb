using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Scheduled notifications addressed to a given number that the provider still holds (status
/// "scheduled") — the follow-ups that must be called off so nothing is sent to a number once the
/// shopper has removed it.
/// </summary>
public class PendingScheduledNotificationsByNumberSpecification : Specification<Notification>
{
    public PendingScheduledNotificationsByNumberSpecification(string toNumber)
    {
        Query.Where(n => n.ToNumber == toNumber
            && n.IsScheduled
            && n.ProviderMessageSid != null
            && n.DeliveryStatus == "scheduled");
    }
}
