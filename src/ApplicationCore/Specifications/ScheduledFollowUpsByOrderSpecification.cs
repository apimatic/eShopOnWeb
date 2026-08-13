using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// The order's follow-up messages that are still scheduled with the provider and have not yet been
/// called off — the ones a cancellation has to reach before they go out. A message that has already
/// been canceled (or has no provider identifier) is excluded.
/// </summary>
public sealed class ScheduledFollowUpsByOrderSpecification : Specification<OrderNotification>
{
    public ScheduledFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n =>
            n.OrderId == orderId &&
            n.IsScheduled &&
            n.ProviderMessageSid != null &&
            n.DeliveryStatus != "canceled");
    }
}
