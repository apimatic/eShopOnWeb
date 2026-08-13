using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Follow-up messages for an order that the provider is still holding to send ("scheduled") — the
/// ones that must be called off when the order is cancelled.
/// </summary>
public class PendingFollowUpsByOrderSpecification : Specification<Notification>
{
    public PendingFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
            && n.Type == NotificationType.DeliveryFollowUp
            && n.ProviderMessageSid != null
            && n.DeliveryStatus == "scheduled");
    }
}
