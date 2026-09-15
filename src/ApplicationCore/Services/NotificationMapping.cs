using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>Shared mapping helpers for notifications: terminal-state checks and view projection.</summary>
public static class NotificationMapping
{
    /// <summary>
    /// A status the provider will not move away from, so there is no point refreshing it. Scheduled and
    /// the in-flight states are deliberately not terminal.
    /// </summary>
    public static bool IsTerminal(NotificationDeliveryStatus status) => status switch
    {
        NotificationDeliveryStatus.Delivered => true,
        NotificationDeliveryStatus.Undelivered => true,
        NotificationDeliveryStatus.Failed => true,
        NotificationDeliveryStatus.Canceled => true,
        NotificationDeliveryStatus.Read => true,
        NotificationDeliveryStatus.NotSent => true,
        NotificationDeliveryStatus.SendError => true,
        _ => false
    };

    public static NotificationView ToView(OrderNotification n) =>
        new(
            n.Id,
            n.OrderId,
            n.Type.ToString(),
            n.Status.ToString(),
            n.ProviderStatusRaw,
            n.ProviderMessageId,
            n.ErrorCode,
            n.ErrorMessage,
            n.IsFollowUp,
            n.ContentDisposed,
            n.Body,
            Mask(n.ToNumber),
            n.ScheduledFor,
            n.SentAt,
            n.CreatedAt,
            n.UpdatedAt);

    /// <summary>Mask a number for display, keeping only the last four digits. Never the full number.</summary>
    private static string? Mask(string? number)
    {
        if (string.IsNullOrEmpty(number))
        {
            return null;
        }

        var digits = number.Where(char.IsDigit).ToArray();
        if (digits.Length <= 4)
        {
            return new string('•', digits.Length);
        }

        var last4 = new string(digits[^4..]);
        return $"{new string('•', digits.Length - 4)}{last4}";
    }
}
