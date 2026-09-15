using System.Globalization;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// The text of each message the shop sends as an order moves. Kept in one place so wording is consistent
/// and free of anything sensitive. Never contains the recipient's number.
/// </summary>
public static class NotificationMessages
{
    public static string For(NotificationKind kind, Order order) => kind switch
    {
        NotificationKind.OrderPlaced => OrderPlaced(order),
        NotificationKind.OrderDispatched => OrderDispatched(order),
        NotificationKind.DeliveryFollowUp => DeliveryFollowUp(order),
        NotificationKind.OrderCancelled => OrderCancelled(order),
        _ => Generic(order.Id)
    };

    public static string OrderPlaced(Order order) =>
        $"eShopOnWeb: your order #{order.Id} has been placed. Order total {Money(order.Total())}. Thank you for shopping with us!";

    public static string OrderDispatched(Order order) =>
        $"eShopOnWeb: good news — your order #{order.Id} is on its way!";

    public static string DeliveryFollowUp(Order order) =>
        $"eShopOnWeb: how did the delivery of your order #{order.Id} go? We'd love your feedback.";

    public static string OrderCancelled(Order order) =>
        $"eShopOnWeb: your order #{order.Id} has been cancelled. If this is unexpected, please contact support.";

    /// <summary>Fallback used when re-sending a message whose stored text was already disposed of.</summary>
    public static string Generic(int orderId) =>
        $"eShopOnWeb: an update about your order #{orderId}.";

    private static string Money(decimal amount) => amount.ToString("C", CultureInfo.GetCultureInfo("en-US"));
}
