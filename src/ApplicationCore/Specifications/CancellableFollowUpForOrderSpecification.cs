using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// The queued delivery follow-up(s) for an order that can still be called off — the message
/// order-cancel must reach the provider to cancel, so a cancelled order never triggers a
/// "how did the delivery go?" text.
/// </summary>
public class CancellableFollowUpForOrderSpecification : Specification<SmsNotification>
{
    public CancellableFollowUpForOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
                         && n.Kind == NotificationKind.DeliveryFollowUp
                         && n.Outcome == NotificationOutcome.Scheduled
                         && n.ProviderSid != null);
    }
}
