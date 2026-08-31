using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Delivery follow-ups for an order that the provider has accepted but not sent yet
/// (last known provider status supplied by the caller, e.g. "scheduled").
/// </summary>
public class PendingFollowUpsByOrderSpecification : Specification<OrderNotification>
{
    public PendingFollowUpsByOrderSpecification(int orderId, string notYetSentProviderStatus)
    {
        Query.Where(n => n.OrderId == orderId
            && n.Kind == NotificationKind.DeliveryFollowUp
            && n.ProviderMessageSid != null
            && n.ProviderStatus == notYetSentProviderStatus);
    }
}
