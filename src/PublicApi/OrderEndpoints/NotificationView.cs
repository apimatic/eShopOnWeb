using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Read model for a notification. Deliberately excludes the destination number and the message body — those
/// are sensitive and are never returned by an endpoint. Carries the provider identifier and current delivery
/// outcome so callers can see where a notification got to.
/// </summary>
public class NotificationView
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string SendState { get; set; } = string.Empty;
    public string? ProviderStatus { get; set; }
    public string? ProviderMessageSid { get; set; }
    public int? ProviderErrorCode { get; set; }
    public string? ProviderErrorMessage { get; set; }
    public bool ContentRedacted { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }
    public DateTimeOffset? ProviderDateSent { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static NotificationView From(Notification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Kind = n.Kind.ToString(),
        SendState = n.SendState.ToString(),
        ProviderStatus = n.ProviderStatus,
        ProviderMessageSid = n.ProviderMessageSid,
        ProviderErrorCode = n.ProviderErrorCode,
        ProviderErrorMessage = n.ProviderErrorMessage,
        ContentRedacted = n.ContentRedacted,
        ScheduledSendAt = n.ScheduledSendAt,
        ProviderDateSent = n.ProviderDateSent,
        CreatedAt = n.CreatedAt
    };
}
