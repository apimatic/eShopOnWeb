using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Sms;

public static class OrderSmsTemplates
{
    public static string For(NotificationKind kind, int orderId) => kind switch
    {
        NotificationKind.OrderPlaced =>
            $"Your eShopOnWeb order #{orderId} has been placed. Thank you for shopping with us.",
        NotificationKind.OrderDispatched =>
            $"Your eShopOnWeb order #{orderId} is on its way.",
        NotificationKind.DeliveryFollowUp =>
            $"How did the delivery of eShopOnWeb order #{orderId} go? We would like to hear how it went.",
        NotificationKind.OrderCancelled =>
            $"Your eShopOnWeb order #{orderId} has been cancelled.",
        _ => $"An update is available for eShopOnWeb order #{orderId}."
    };
}
