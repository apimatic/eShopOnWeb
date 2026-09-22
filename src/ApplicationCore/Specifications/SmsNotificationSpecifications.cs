using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SmsNotificationsByOrderSpecification : Specification<SmsNotification>
{
    public SmsNotificationsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId);
    }
}

public class SmsNotificationByIdSpecification : Specification<SmsNotification>
{
    public SmsNotificationByIdSpecification(int notificationId)
    {
        Query.Where(n => n.Id == notificationId);
    }
}

/// <summary>Scheduled follow-up notifications for an order that have not yet gone out (or been cancelled).</summary>
public class ScheduledFollowUpsByOrderSpecification : Specification<SmsNotification>
{
    public ScheduledFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
            && n.Kind == NotificationKind.DeliveryFollowUp
            && n.State == NotificationState.Scheduled);
    }
}

/// <summary>Notifications this application believes it sent whose provider send time falls in range.</summary>
public class SmsNotificationsSentInRangeSpecification : Specification<SmsNotification>
{
    public SmsNotificationsSentInRangeSpecification(System.DateTimeOffset from, System.DateTimeOffset to)
    {
        Query.Where(n => n.ProviderSid != null
            && n.ProviderDateSent != null
            && n.ProviderDateSent >= from
            && n.ProviderDateSent <= to);
    }
}

/// <summary>
/// Notifications carrying a provider SID whose row was created near a range — the bounded set to
/// refresh the provider send-time on before reconciling, so both sides filter on the same clock.
/// </summary>
public class SmsNotificationsWithProviderSidCreatedBetweenSpecification : Specification<SmsNotification>
{
    public SmsNotificationsWithProviderSidCreatedBetweenSpecification(System.DateTimeOffset createdFrom, System.DateTimeOffset createdTo)
    {
        Query.Where(n => n.ProviderSid != null
            && n.CreatedAt >= createdFrom
            && n.CreatedAt <= createdTo);
    }
}
