using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Composes the SMS text for each order event. Kept free of any personal data beyond the order id.
/// </summary>
public static class NotificationMessages
{
    public static string For(NotificationKind kind, int orderId) => kind switch
    {
        NotificationKind.OrderPlaced => $"eShop: thanks! Your order #{orderId} has been placed.",
        NotificationKind.OrderDispatched => $"eShop: good news — your order #{orderId} is on its way!",
        NotificationKind.OrderCancelled => $"eShop: your order #{orderId} has been cancelled.",
        NotificationKind.DeliveryFollowUp => $"eShop: how did the delivery of order #{orderId} go? Reply to let us know.",
        _ => $"eShop: an update on your order #{orderId}."
    };
}
