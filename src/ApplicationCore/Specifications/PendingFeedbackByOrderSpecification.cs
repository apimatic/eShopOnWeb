using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// The scheduled delivery-feedback follow-ups for an order that have not yet gone out or been called off —
/// the ones a cancellation must cancel with the provider.
/// </summary>
public class PendingFeedbackByOrderSpecification : Specification<OrderNotification>
{
    public PendingFeedbackByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
            && n.Type == NotificationType.DeliveryFeedback
            && n.IsScheduled
            && n.SendState == NotificationSendState.Accepted);
    }
}
