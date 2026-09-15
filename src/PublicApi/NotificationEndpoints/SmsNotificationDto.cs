using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// The operator/shopper view of one message and what became of it. Deliberately omits the destination
/// number and the message body, which are never returned by the API.
/// </summary>
public class SmsNotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;

    /// <summary>The provider's own identifier for the message, once created.</summary>
    public string? ProviderSid { get; set; }

    /// <summary>The provider's current delivery outcome for the message.</summary>
    public string? Status { get; set; }

    public int? ProviderErrorCode { get; set; }
    public string? ProviderErrorMessage { get; set; }

    public DateTimeOffset? ScheduledFor { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public bool ContentDisposed { get; set; }
    public bool IsResend { get; set; }
    public int? ResendOfNotificationId { get; set; }

    public static SmsNotificationDto From(SmsNotification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Kind = n.Kind.ToString(),
        ProviderSid = n.ProviderSid,
        Status = n.ProviderStatus,
        ProviderErrorCode = n.ProviderErrorCode,
        ProviderErrorMessage = n.ProviderErrorMessage,
        ScheduledFor = n.ScheduledFor,
        SentAt = n.SentAt,
        CreatedAt = n.CreatedAt,
        ContentDisposed = n.ContentDisposed,
        IsResend = n.ResendOfNotificationId.HasValue,
        ResendOfNotificationId = n.ResendOfNotificationId
    };
}
