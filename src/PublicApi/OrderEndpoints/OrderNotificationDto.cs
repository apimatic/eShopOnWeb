using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// What eShop knows about one message it sent about an order — including the provider's current
/// delivery outcome. Never includes the destination number.
/// </summary>
public class OrderNotificationDto
{
    /// <summary>Identifier the operator endpoints act on.</summary>
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int? ErrorCode { get; set; }
    public bool Scheduled { get; set; }
    public bool ContentRedacted { get; set; }
    /// <summary>Provider's message identifier (not a phone number).</summary>
    public string? ProviderSid { get; set; }
    /// <summary>Message text; null once its content has been disposed of.</summary>
    public string? Body { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public static OrderNotificationDto FromEntity(SmsNotification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Kind = n.Kind.ToString(),
        Status = n.Status,
        ErrorCode = n.ErrorCode,
        Scheduled = n.IsScheduled,
        ContentRedacted = n.ContentRedacted,
        ProviderSid = n.ProviderSid,
        Body = n.Body,
        CreatedAt = n.CreatedAt,
        UpdatedAt = n.UpdatedAt
    };
}
