using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Delivery follow-up messages for an order that were accepted by the provider and
/// have not been sent yet (still scheduled with the provider).
/// </summary>
public class PendingFollowUpsByOrderSpecification : Specification<OrderNotification>
{
    public PendingFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
            && n.NotificationType == NotificationType.DeliveryFollowUp
            && n.AcceptedByProvider
            && n.ProviderMessageSid != null
            && n.ProviderStatus == "scheduled");
    }
}
