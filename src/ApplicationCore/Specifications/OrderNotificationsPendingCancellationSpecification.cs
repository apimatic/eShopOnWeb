using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderNotificationsPendingCancellationSpecification : Specification<OrderNotification>
{
    public OrderNotificationsPendingCancellationSpecification()
    {
        Query.Where(n => n.Status == OrderNotificationStatus.CancellationPending
            && n.ProviderMessageSid != null
            && n.CancellationCompletedAt == null)
            .OrderBy(n => n.CancellationRequestedAt)
            .ThenBy(n => n.Id);
    }
}
