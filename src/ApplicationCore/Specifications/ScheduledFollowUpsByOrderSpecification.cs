using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// The follow-up messages for an order that are still scheduled at the provider (have a provider id
/// and have not yet gone out) — the ones a cancellation must call off.
/// </summary>
public sealed class ScheduledFollowUpsByOrderSpecification : Specification<Notification>
{
    public ScheduledFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n =>
            n.OrderId == orderId &&
            n.Kind == NotificationKind.DeliveryFollowUp &&
            n.Status == NotificationStatus.Scheduled &&
            n.ProviderMessageSid != null);
    }
}
