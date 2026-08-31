using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Provider-scheduled follow-ups for an order that have not yet gone out.</summary>
public class ScheduledFollowUpsByOrderSpecification : Specification<OrderNotification>
{
    public const string ScheduledStatus = "scheduled";

    public ScheduledFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
            && n.NotificationType == NotificationType.DeliveryFollowUp
            && n.ProviderStatus == ScheduledStatus
            && n.ProviderMessageId != null);
    }
}

/// <summary>Provider-scheduled messages to a contact number that have not yet gone out.</summary>
public class ScheduledNotificationsByContactNumberSpecification : Specification<OrderNotification>
{
    public ScheduledNotificationsByContactNumberSpecification(int contactNumberId)
    {
        Query.Where(n => n.ContactNumberId == contactNumberId
            && n.ProviderStatus == ScheduledFollowUpsByOrderSpecification.ScheduledStatus
            && n.ProviderMessageId != null);
    }
}
