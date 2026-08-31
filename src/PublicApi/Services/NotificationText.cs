using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.Services;

public static class NotificationText
{
    public static string OrderPlaced(Order order) =>
        $"eShopOnWeb: thank you! Your order #{order.Id} (${order.Total():0.00}) has been placed. We'll text you when it's on its way.";

    public static string OrderDispatched(Order order) =>
        $"eShopOnWeb: good news — your order #{order.Id} has been dispatched and is on its way.";

    public static string DeliveryFollowUp(Order order) =>
        $"eShopOnWeb: your order #{order.Id} should have arrived by now. How did the delivery go?";

    public static string OrderCancelled(Order order) =>
        $"eShopOnWeb: your order #{order.Id} has been cancelled. If this is unexpected, please contact support.";
}
