using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// What a caller sees about one message: enough of the state eShop and the provider own to act on
/// it and report on it. The destination number is masked — the raw number is never surfaced.
/// </summary>
public class NotificationDto
{
    /// <summary>The identifier the operator endpoints (resend, dispose) act on.</summary>
    public int NotificationId { get; set; }

    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;

    /// <summary>eShop's normalized delivery outcome (e.g. Delivered, Undelivered, Scheduled).</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>The provider's own raw status string, when known.</summary>
    public string? ProviderStatus { get; set; }

    /// <summary>The provider's message identifier, when a message was created.</summary>
    public string? ProviderSid { get; set; }

    /// <summary>The provider's delivery error code, when a message failed or was not delivered.</summary>
    public int? ErrorCode { get; set; }

    /// <summary>The destination, masked to its last few digits.</summary>
    public string To { get; set; } = string.Empty;

    public bool ContentDisposed { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }

    public static NotificationDto FromEntity(Notification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Kind = n.Kind.ToString(),
        Status = n.Status.ToString(),
        ProviderStatus = n.ProviderStatus,
        ProviderSid = n.ProviderSid,
        ErrorCode = n.ProviderErrorCode,
        To = PhoneNumberMask.Mask(n.ToNumber),
        ContentDisposed = n.ContentDisposed,
        CreatedAt = n.CreatedAt,
        SentAt = n.SentAt,
        ScheduledSendAt = n.ScheduledSendAt
    };
}

/// <summary>Masks a phone number to its last four digits so full numbers are never surfaced.</summary>
public static class PhoneNumberMask
{
    public static string Mask(string? number)
    {
        if (string.IsNullOrEmpty(number))
        {
            return string.Empty;
        }

        var last4 = number.Length <= 4 ? number : number.Substring(number.Length - 4);
        return $"••••••{last4}";
    }
}
