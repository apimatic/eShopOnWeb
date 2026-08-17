using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// A message about an order and what became of it. Carries its own <see cref="NotificationId"/> —
/// the identifier the operator endpoints act on — and the provider's identifier and current outcome.
/// The destination is masked; a shopper's full number is never echoed here.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ProviderStatus { get; set; }
    public string? ProviderMessageId { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string To { get; set; } = string.Empty;
    public bool ContentRedacted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public DateTimeOffset? SentAt { get; set; }

    public static NotificationDto From(SmsNotification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Kind = n.Kind.ToString(),
        Status = n.Status.ToString(),
        ProviderStatus = n.ProviderStatus,
        ProviderMessageId = n.ProviderMessageId,
        ErrorCode = n.ErrorCode,
        ErrorMessage = n.ErrorMessage,
        To = PhoneMask.Mask(n.ToNumber),
        ContentRedacted = n.ContentRedacted,
        CreatedAt = n.CreatedAt,
        ScheduledFor = n.ScheduledFor,
        SentAt = n.SentAt
    };
}
