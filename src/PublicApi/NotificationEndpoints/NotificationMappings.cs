using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>DTO describing a single SMS and what became of it. The destination number is masked.</summary>
public class SmsNotificationDto
{
    public int NotificationId { get; set; }
    public int? OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string ToNumberMasked { get; set; } = string.Empty;
    public string? ProviderSid { get; set; }
    public string? ProviderStatus { get; set; }
    public int? ProviderErrorCode { get; set; }
    public string? ProviderErrorMessage { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }
    public DateTimeOffset? ProviderDateSent { get; set; }
    public bool ContentDisposed { get; set; }
    public int? ResendOfNotificationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public static class NotificationMappings
{
    public static SmsNotificationDto ToDto(this SmsNotification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Kind = n.Kind.ToString(),
        Outcome = n.Outcome.ToString(),
        ToNumberMasked = MaskNumber(n.ToNumber),
        ProviderSid = n.ProviderSid,
        ProviderStatus = n.ProviderStatus,
        ProviderErrorCode = n.ProviderErrorCode,
        ProviderErrorMessage = n.ProviderErrorMessage,
        ScheduledSendAt = n.ScheduledSendAt,
        ProviderDateSent = n.ProviderDateSent,
        ContentDisposed = n.ContentDisposed,
        ResendOfNotificationId = n.ResendOfNotificationId,
        CreatedAt = n.CreatedAt
    };

    /// <summary>Masks all but the leading "+cc" and the last two digits, so numbers never leak whole.</summary>
    public static string MaskNumber(string? number)
    {
        if (string.IsNullOrEmpty(number) || number.Length <= 4)
            return "(hidden)";
        var visiblePrefix = number.StartsWith("+") ? 2 : 1;
        var start = number.Substring(0, Math.Min(visiblePrefix, number.Length));
        var end = number.Substring(number.Length - 2);
        return $"{start}{new string('*', Math.Max(0, number.Length - visiblePrefix - 2))}{end}";
    }
}
