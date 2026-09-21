using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// What became of one message about an order. Carries the provider's identifier and current
/// delivery outcome; deliberately omits the destination phone number.
/// </summary>
public class OrderNotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? MessageSid { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsScheduledFollowUp { get; set; }
    public bool ContentRedacted { get; set; }
    public DateTimeOffset CreatedDate { get; set; }
    public DateTimeOffset? ProviderDateSent { get; set; }

    public static OrderNotificationDto From(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Type = n.Type.ToString(),
        Status = n.Status,
        MessageSid = n.MessageSid,
        ErrorCode = n.ErrorCode,
        ErrorMessage = n.ErrorMessage,
        IsScheduledFollowUp = n.IsScheduledFollowUp,
        ContentRedacted = n.ContentRedacted,
        CreatedDate = n.CreatedDate,
        ProviderDateSent = n.ProviderDateSent
    };
}
