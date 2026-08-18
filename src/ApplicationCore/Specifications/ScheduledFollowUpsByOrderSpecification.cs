using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>The scheduled delivery-feedback follow-ups for an order that are still eligible to be called off
/// (queued with the provider, not already sent, failed or cancelled).</summary>
public class ScheduledFollowUpsByOrderSpecification : Specification<Notification>
{
    public ScheduledFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
            && n.IsScheduled
            && !n.SendFailed
            && n.ProviderSid != null
            && n.DeliveryStatus == "scheduled");
    }
}
