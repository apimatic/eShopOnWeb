namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Which order event a notification message was sent for.
/// </summary>
public enum NotificationType
{
    /// <summary>Order was placed.</summary>
    OrderPlaced = 0,

    /// <summary>Order was dispatched / is on its way.</summary>
    OrderDispatched = 1,

    /// <summary>Post-delivery "how did it go?" follow-up, scheduled with the provider for later.</summary>
    DeliveryFollowUp = 2,

    /// <summary>Order was cancelled.</summary>
    OrderCancelled = 3,

    /// <summary>An operator re-sent a message that had not reached the shopper.</summary>
    Resend = 4
}
