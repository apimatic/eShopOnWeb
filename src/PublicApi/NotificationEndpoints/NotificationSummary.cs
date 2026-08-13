using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// The operator/owner view of a single notification: its identifier (what the operator endpoints act on),
/// what it was, its current delivery outcome, and the provider's identifier for it. The destination number
/// is deliberately never included.
/// </summary>
public record NotificationSummary(
    int NotificationId,
    string Kind,
    string? DeliveryStatus,
    string? ProviderMessageSid,
    string? Body,
    bool ContentDisposed,
    int? ProviderErrorCode,
    string? ProviderErrorMessage,
    int? ResendOfNotificationId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt)
{
    public static NotificationSummary From(OrderNotification n) => new(
        NotificationId: n.Id,
        Kind: n.Kind.ToString(),
        DeliveryStatus: n.DeliveryStatus,
        ProviderMessageSid: n.ProviderMessageSid,
        Body: n.Body,
        ContentDisposed: n.ContentDisposed,
        ProviderErrorCode: n.ProviderErrorCode,
        ProviderErrorMessage: n.ProviderErrorMessage,
        ResendOfNotificationId: n.ResendOfNotificationId,
        CreatedAt: n.CreatedAt,
        SentAt: n.SentAt);
}
