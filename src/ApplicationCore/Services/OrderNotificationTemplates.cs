using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public static class OrderNotificationTemplates
{
    public static string BodyFor(OrderNotificationKind kind, int orderId)
    {
        return kind switch
        {
            OrderNotificationKind.OrderPlaced =>
                $"Your eShop order #{orderId} has been placed. Thank you for shopping with us.",
            OrderNotificationKind.OrderDispatched =>
                $"Your eShop order #{orderId} has been dispatched and is on its way.",
            OrderNotificationKind.DeliveryFollowUp =>
                $"How did the delivery of your eShop order #{orderId} go? We would love to hear how it went.",
            OrderNotificationKind.OrderCancelled =>
                $"Your eShop order #{orderId} has been cancelled.",
            _ => $"Update on your eShop order #{orderId}."
        };
    }
}
