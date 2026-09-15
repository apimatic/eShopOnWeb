using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Composes the text of each message the shop sends. Bodies deliberately carry no phone number and no
/// other personal contact detail.
/// </summary>
public static class NotificationMessages
{
    public static string For(NotificationKind kind, Order order) => kind switch
    {
        NotificationKind.OrderPlaced =>
            $"eShopOnWeb: thanks! Your order #{order.Id} has been placed. Total: {order.Total():C}.",
        NotificationKind.OrderDispatched =>
            $"eShopOnWeb: good news — your order #{order.Id} is on its way.",
        NotificationKind.DeliveryFollowUp =>
            $"eShopOnWeb: how did the delivery of your order #{order.Id} go? We'd love your feedback.",
        NotificationKind.OrderCancelled =>
            $"eShopOnWeb: your order #{order.Id} has been cancelled. If this is unexpected, please contact us.",
        _ => $"eShopOnWeb: an update about your order #{order.Id}."
    };
}
