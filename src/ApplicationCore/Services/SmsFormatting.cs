using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>Builds the SMS bodies for each order-lifecycle event.</summary>
public static class SmsMessageTemplates
{
    public static string For(SmsNotificationKind kind, Order order) => kind switch
    {
        SmsNotificationKind.OrderPlaced =>
            $"eShopOnWeb: Thanks for your order #{order.Id}! We've received it (total ${order.Total():0.00}) and will let you know when it ships.",
        SmsNotificationKind.OrderDispatched =>
            $"eShopOnWeb: Good news - your order #{order.Id} is on its way!",
        SmsNotificationKind.DeliveryFollowUp =>
            $"eShopOnWeb: How did the delivery of your order #{order.Id} go? We'd love your feedback.",
        SmsNotificationKind.OrderCancelled =>
            $"eShopOnWeb: Your order #{order.Id} has been cancelled. If this is unexpected, please contact support.",
        _ => $"eShopOnWeb: Update on your order #{order.Id}."
    };
}

/// <summary>Masks a phone number for display so full numbers never leave the system in the clear.</summary>
public static class PhoneMask
{
    public static string Mask(string? e164)
    {
        if (string.IsNullOrWhiteSpace(e164))
            return "unknown";

        var digitsShown = 4;
        if (e164.Length <= digitsShown)
            return new string('*', e164.Length);

        var suffix = e164.Substring(e164.Length - digitsShown);
        var prefix = e164.StartsWith("+") ? "+" : string.Empty;
        return $"{prefix}{new string('*', e164.Length - digitsShown - prefix.Length)}{suffix}";
    }
}
