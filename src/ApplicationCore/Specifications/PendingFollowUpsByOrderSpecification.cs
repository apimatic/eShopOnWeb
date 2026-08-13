using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A follow-up message for an order that is still scheduled with the provider (has a SID and has not
/// yet been sent or canceled) — the messages a cancellation must call off.
/// </summary>
public class PendingFollowUpsByOrderSpecification : Specification<Notification>
{
    public PendingFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
            && n.Type == NotificationType.DeliveryFollowUp
            && n.ProviderMessageSid != null
            && n.Status == NotificationStatuses.Scheduled);
    }
}
