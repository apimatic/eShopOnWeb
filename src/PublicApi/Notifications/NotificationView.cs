using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

/// <summary>
/// What a caller sees about a notification: its own <see cref="NotificationId"/> (what the operator
/// endpoints act on), which order it relates to, and where it got to with the provider. The
/// destination number and message body are deliberately not exposed.
/// </summary>
public sealed record NotificationView(
    int NotificationId,
    int OrderId,
    string Type,
    string DeliveryStatus,
    string? ProviderMessageSid,
    bool ContentRedacted,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset CreatedDate,
    DateTimeOffset? ProviderDateSent);

public static class NotificationMapping
{
    public static NotificationView ToView(Notification n) => new(
        n.Id,
        n.OrderId,
        n.Type.ToString(),
        n.DeliveryStatus,
        n.ProviderMessageSid,
        n.ContentRedacted,
        n.ErrorCode,
        n.ErrorMessage,
        n.CreatedDate,
        n.ProviderDateSent);
}
