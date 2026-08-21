namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public static class OrderSmsTemplates
{
    public static string For(OrderNotificationKind kind, int orderId) => kind switch
    {
        OrderNotificationKind.OrderPlaced =>
            $"eShopOnWeb: Your order #{orderId} has been placed. We will text you when it ships.",
        OrderNotificationKind.OrderDispatched =>
            $"eShopOnWeb: Order #{orderId} is on its way.",
        OrderNotificationKind.DeliveryFollowUp =>
            $"eShopOnWeb: How did the delivery of order #{orderId} go?",
        OrderNotificationKind.OrderCancelled =>
            $"eShopOnWeb: Order #{orderId} has been cancelled.",
        _ => $"eShopOnWeb: Update for order #{orderId}."
    };
}
