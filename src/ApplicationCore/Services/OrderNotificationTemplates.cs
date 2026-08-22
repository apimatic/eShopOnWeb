using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public static class OrderNotificationTemplates
{
    public static string For(NotificationKind kind, int orderId) => kind switch
    {
        NotificationKind.OrderPlaced => $"eShopOnWeb: Your order #{orderId} has been placed. Thank you!",
        NotificationKind.OrderDispatched => $"eShopOnWeb: Order #{orderId} is on its way.",
        NotificationKind.DeliveryFollowUp => $"eShopOnWeb: How did the delivery go for order #{orderId}?",
        NotificationKind.OrderCancelled => $"eShopOnWeb: Order #{orderId} has been cancelled.",
        NotificationKind.Resend => $"eShopOnWeb: An update about order #{orderId}.",
        _ => $"eShopOnWeb: An update about order #{orderId}."
    };
}
