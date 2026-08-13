using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// A notification as returned to callers. Carries the provider's identifier and current delivery
/// outcome so operator endpoints can act on it. The destination number is deliberately not exposed.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Type { get; set; } = string.Empty;

    /// <summary>Current delivery outcome — a provider wire status once sent, else a local marker.</summary>
    public string Status { get; set; } = string.Empty;

    public string? ProviderMessageSid { get; set; }
    public int? ProviderErrorCode { get; set; }
    public string? ProviderErrorMessage { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }
    public bool ContentDisposed { get; set; }

    /// <summary>Message text; null once the content has been disposed.</summary>
    public string? Body { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public static NotificationDto FromEntity(Notification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Type = n.Type.ToString(),
        Status = n.DeliveryStatus,
        ProviderMessageSid = n.ProviderMessageSid,
        ProviderErrorCode = n.ProviderErrorCode,
        ProviderErrorMessage = n.ProviderErrorMessage,
        ScheduledSendAt = n.ScheduledSendAt,
        ContentDisposed = n.ContentDisposed,
        Body = n.Body,
        CreatedAt = n.CreatedAt
    };
}
