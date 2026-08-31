using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>The text shoppers receive as their order moves.</summary>
public static class NotificationMessages
{
    public static string OrderPlaced(Order order) =>
        $"eShopOnWeb: thanks! Your order #{order.Id} was placed. Total: ${order.Total():0.00}. We'll text you when it's on its way.";

    public static string OrderDispatched(Order order) =>
        $"eShopOnWeb: good news — your order #{order.Id} has been dispatched and is on its way.";

    public static string DeliveryFollowUp(Order order) =>
        $"eShopOnWeb: your order #{order.Id} should have arrived by now. How did the delivery go?";

    public static string OrderCancelled(Order order) =>
        $"eShopOnWeb: your order #{order.Id} has been cancelled. If this is unexpected, please contact support.";
}
