using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Composes the short SMS text that goes out for each order event. Kept in one place so a re-send
/// can reproduce the message even after its stored content has been disposed of.
/// </summary>
public static class NotificationMessageComposer
{
    public static string Compose(NotificationType type, int orderId) => type switch
    {
        NotificationType.OrderPlaced =>
            $"eShopOnWeb: Thanks for your order! Order #{orderId} has been placed.",
        NotificationType.OrderDispatched =>
            $"eShopOnWeb: Good news - your order #{orderId} is on its way!",
        NotificationType.DeliveryFollowUp =>
            $"eShopOnWeb: How did the delivery of order #{orderId} go? We'd love your feedback.",
        NotificationType.OrderCancelled =>
            $"eShopOnWeb: Your order #{orderId} has been cancelled. Contact us if this is unexpected.",
        _ => $"eShopOnWeb: Update on your order #{orderId}."
    };
}
