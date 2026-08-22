namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public static class OrderNotificationTemplates
{
    public static string For(OrderNotificationKind kind, int orderId) => kind switch
    {
        OrderNotificationKind.OrderPlaced => $"eShopOnWeb: your order #{orderId} has been placed.",
        OrderNotificationKind.OrderDispatched => $"eShopOnWeb: your order #{orderId} is on its way.",
        OrderNotificationKind.DeliveryFollowUp => $"eShopOnWeb: how did the delivery of order #{orderId} go?",
        OrderNotificationKind.OrderCancelled => $"eShopOnWeb: your order #{orderId} has been cancelled.",
        _ => $"eShopOnWeb: an update on order #{orderId}."
    };
}
