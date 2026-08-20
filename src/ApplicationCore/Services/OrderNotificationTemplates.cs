using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public static class OrderNotificationTemplates
{
    public static string For(NotificationKind kind, int orderId) => kind switch
    {
        NotificationKind.OrderPlaced =>
            $"Your eShopOnWeb order #{orderId} has been placed. We'll update you as it progresses.",
        NotificationKind.OrderDispatched =>
            $"Your eShopOnWeb order #{orderId} is on its way.",
        NotificationKind.DeliveryFollowUp =>
            $"How did the delivery of your eShopOnWeb order #{orderId} go?",
        NotificationKind.OrderCancelled =>
            $"Your eShopOnWeb order #{orderId} has been cancelled.",
        _ => $"Update on your eShopOnWeb order #{orderId}."
    };
}
