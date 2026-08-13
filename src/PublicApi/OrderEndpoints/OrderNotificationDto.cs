using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// What a caller sees about one message: its own identifier (which the operator endpoints act on), the
/// provider's identifier and current delivery outcome, and enough metadata to understand it. The
/// destination number is only ever shown masked, and never in full.
/// </summary>
public class OrderNotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;

    /// <summary>The provider's message identifier, or null if the provider never created one.</summary>
    public string? ProviderMessageSid { get; set; }

    /// <summary>The current delivery outcome as owned by the provider.</summary>
    public string Status { get; set; } = string.Empty;

    public bool ReachedRecipient { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }

    public bool IsScheduled { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }

    public bool ContentRedacted { get; set; }

    /// <summary>Masked destination, e.g. <c>+1******1588</c>. Never the full number.</summary>
    public string MaskedTo { get; set; } = string.Empty;

    public int? ResendOfNotificationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public static OrderNotificationDto FromEntity(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Kind = n.Kind.ToString(),
        ProviderMessageSid = n.ProviderMessageSid,
        Status = n.Status,
        ReachedRecipient = n.ReachedRecipient(),
        ErrorCode = n.ErrorCode,
        ErrorMessage = n.ErrorMessage,
        IsScheduled = n.IsScheduled,
        ScheduledSendAt = n.ScheduledSendAt,
        ContentRedacted = n.ContentRedacted,
        MaskedTo = Mask(n.ToPhoneNumber),
        ResendOfNotificationId = n.ResendOfNotificationId,
        CreatedAt = n.CreatedAt,
        UpdatedAt = n.UpdatedAt
    };

    private static string Mask(string number)
    {
        if (string.IsNullOrEmpty(number)) return string.Empty;
        var last4 = number.Length <= 4 ? number : number[^4..];
        var prefix = number.StartsWith("+") ? "+" : string.Empty;
        return $"{prefix}******{last4}";
    }
}
