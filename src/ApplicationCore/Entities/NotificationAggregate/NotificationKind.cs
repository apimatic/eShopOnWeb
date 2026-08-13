namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// What a given <see cref="OrderNotification"/> is telling the shopper about.
/// </summary>
public enum NotificationKind
{
    /// <summary>Confirmation that the order was placed.</summary>
    OrderPlaced = 0,

    /// <summary>Confirmation that the order has been dispatched / is on its way.</summary>
    OrderDispatched = 1,

    /// <summary>The "how did the delivery go?" follow-up, scheduled with the provider for later.</summary>
    DeliveryFollowUp = 2,

    /// <summary>Confirmation that the order was cancelled.</summary>
    OrderCancelled = 3
}
