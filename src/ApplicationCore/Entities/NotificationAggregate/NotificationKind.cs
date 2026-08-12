namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>Why a given SMS was sent, as the order moved.</summary>
public enum NotificationKind
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    /// <summary>The "how did delivery go?" message scheduled with the provider for a few days after dispatch.</summary>
    DeliveryFollowUp = 2,
    OrderCancelled = 3,
    /// <summary>An operator-initiated re-send of a message that did not reach the shopper.</summary>
    Resend = 4
}
