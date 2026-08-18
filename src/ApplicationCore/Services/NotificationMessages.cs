namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// The wording that goes out to shoppers. Message text never contains the destination number.
/// </summary>
public static class NotificationMessages
{
    public static string OrderPlaced(int orderId) =>
        $"eShopOnWeb: Thanks for your order! Order #{orderId} has been placed and is being prepared.";

    public static string OrderDispatched(int orderId) =>
        $"eShopOnWeb: Good news - your order #{orderId} is on its way!";

    public static string DeliveryFollowUp(int orderId) =>
        $"eShopOnWeb: How did the delivery of your order #{orderId} go? We'd love your feedback.";

    public static string OrderCancelled(int orderId) =>
        $"eShopOnWeb: Your order #{orderId} has been cancelled. If this is unexpected, please contact support.";

    /// <summary>The wording for a given kind, used when resending a message whose content was disposed of.</summary>
    public static string ForKind(Entities.NotificationAggregate.NotificationKind kind, int orderId) => kind switch
    {
        Entities.NotificationAggregate.NotificationKind.OrderPlaced => OrderPlaced(orderId),
        Entities.NotificationAggregate.NotificationKind.OrderDispatched => OrderDispatched(orderId),
        Entities.NotificationAggregate.NotificationKind.DeliveryFollowUp => DeliveryFollowUp(orderId),
        Entities.NotificationAggregate.NotificationKind.OrderCancelled => OrderCancelled(orderId),
        _ => $"eShopOnWeb: An update about your order #{orderId}."
    };
}
