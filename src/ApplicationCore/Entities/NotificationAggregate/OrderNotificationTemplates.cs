namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public static class OrderNotificationTemplates
{
    public static string For(NotificationKind kind, int orderId) => kind switch
    {
        NotificationKind.OrderPlaced => $"Your eShopOnWeb order #{orderId} has been placed.",
        NotificationKind.OrderDispatched => $"Your eShopOnWeb order #{orderId} is on its way.",
        NotificationKind.DeliveryFollowUp => $"How did the delivery of your eShopOnWeb order #{orderId} go?",
        NotificationKind.OrderCancelled => $"Your eShopOnWeb order #{orderId} has been cancelled.",
        _ => $"An update is available for your eShopOnWeb order #{orderId}."
    };
}
