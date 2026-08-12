using System;
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// A message about an order and what became of it. Carries the provider's identifier and current
/// delivery outcome so operator endpoints can act on it. The destination number is deliberately
/// not exposed.
/// </summary>
public class NotificationDto
{
    /// <summary>Identifier the operator endpoints act on.</summary>
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public int? ProviderErrorCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }
    public bool ContentDisposed { get; set; }

    public static NotificationDto FromView(OrderNotificationView view) => new()
    {
        NotificationId = view.NotificationId,
        OrderId = view.OrderId,
        Kind = view.Kind.ToString(),
        Status = view.Status.ToString(),
        ProviderMessageSid = view.ProviderMessageSid,
        ProviderErrorCode = view.ProviderErrorCode,
        CreatedAt = view.CreatedAt,
        ScheduledSendAt = view.ScheduledSendAt,
        ContentDisposed = view.ContentDisposed
    };
}
