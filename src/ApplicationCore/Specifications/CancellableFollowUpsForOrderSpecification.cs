using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Delivery follow-ups for an order that are still queued with the provider
/// (status "scheduled") and can therefore still be called off.
/// </summary>
public class CancellableFollowUpsForOrderSpecification : Specification<OrderNotification>
{
    public const string ProviderScheduledStatus = "scheduled";

    public CancellableFollowUpsForOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
            && n.Kind == NotificationKind.DeliveryFollowUp
            && n.Status == ProviderScheduledStatus
            && n.ProviderMessageSid != null);
    }
}
