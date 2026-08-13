using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// The caller-facing view of a notification. Carries its own <see cref="NotificationId"/> — the id the
/// operator endpoints (resend, content disposal) act on — plus the provider's identifier and current
/// delivery outcome. The destination number is intentionally never exposed here.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string DeliveryStatus { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public bool IsScheduled { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public bool ContentDisposed { get; set; }
    public int? ProviderErrorCode { get; set; }
    public string? ProviderErrorMessage { get; set; }
    public DateTimeOffset CreatedDate { get; set; }

    public static NotificationDto From(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Kind = n.Kind.ToString(),
        DeliveryStatus = n.DeliveryStatus,
        ProviderMessageSid = n.ProviderMessageSid,
        IsScheduled = n.IsScheduled,
        ScheduledFor = n.ScheduledFor,
        ContentDisposed = n.ContentDisposed,
        ProviderErrorCode = n.ProviderErrorCode,
        ProviderErrorMessage = n.ProviderErrorMessage,
        CreatedDate = n.CreatedDate
    };
}
