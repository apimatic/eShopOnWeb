namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>What a given SMS was about.</summary>
public enum NotificationKind
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    /// <summary>The "how did the delivery go?" message, queued with the provider for a few days after dispatch.</summary>
    DeliveryFollowUp = 2,
    OrderCancelled = 3,
    /// <summary>An operator re-send of a message that did not reach the shopper.</summary>
    Resend = 4
}
