namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Why a notification was sent. Distinguishes the messages that go out as an order moves, the
/// scheduled delivery follow-up, and an operator re-send.
/// </summary>
public enum NotificationKind
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    DeliveryFollowUp = 2,
    OrderCancelled = 3,
    Resend = 4
}
