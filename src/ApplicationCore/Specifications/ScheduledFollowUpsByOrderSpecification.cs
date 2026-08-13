using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// The delivery follow-up messages for an order that are still scheduled with the provider and have not
/// yet gone out — the ones a cancellation must call off so they never reach the shopper.
/// </summary>
public class ScheduledFollowUpsByOrderSpecification : Specification<OrderNotification>
{
    public ScheduledFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
            && n.Kind == NotificationKind.DeliveryFollowUp
            && n.ProviderMessageSid != null
            && n.DeliveryStatus == SmsDeliveryStatus.Scheduled);
    }
}
