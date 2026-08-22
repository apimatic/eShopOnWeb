namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public static class OrderNotificationMessages
{
    public static string ForKind(OrderNotificationKind kind, int orderId, decimal total) => kind switch
    {
        OrderNotificationKind.OrderPlaced =>
            $"Your eShopOnWeb order {orderId} has been placed. Total: {total:0.00}.",
        OrderNotificationKind.OrderDispatched =>
            $"Your eShopOnWeb order {orderId} has been dispatched and is on its way.",
        OrderNotificationKind.DeliveryFollowUp =>
            $"How did the delivery of eShopOnWeb order {orderId} go?",
        OrderNotificationKind.OrderCancelled =>
            $"Your eShopOnWeb order {orderId} has been cancelled.",
        _ => $"An update is available for eShopOnWeb order {orderId}."
    };
}
