namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Why a given SMS was sent to the shopper. A resend reuses the type of the message it re-sends.
/// </summary>
public enum NotificationType
{
    /// <summary>"Your order was placed."</summary>
    OrderPlaced = 0,

    /// <summary>"Your order is on its way."</summary>
    OrderDispatched = 1,

    /// <summary>"Your order was cancelled."</summary>
    OrderCancelled = 2,

    /// <summary>"How did the delivery go?" — scheduled with the provider for a few days after dispatch.</summary>
    DeliveryFollowUp = 3
}
