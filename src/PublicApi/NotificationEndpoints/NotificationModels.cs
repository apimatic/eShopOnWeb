using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// A notification as reported over the API. Carries its own <see cref="NotificationId"/> (what the operator
/// endpoints act on) and the provider-owned state — message identifier and current delivery outcome.
/// The destination number is masked; the shopper's number is never returned in full or logged.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Type { get; set; } = string.Empty;

    /// <summary>Where the message got to (the provider's delivery outcome, or a local sentinel).</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>The provider's identifier for the message, when it accepted one.</summary>
    public string? ProviderMessageSid { get; set; }

    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsScheduled { get; set; }
    public bool ContentRedacted { get; set; }

    /// <summary>Masked destination (only the last digits are shown).</summary>
    public string? ToPhoneNumberMasked { get; set; }

    public int? ResendOfNotificationId { get; set; }
    public DateTimeOffset CreatedDate { get; set; }
    public DateTimeOffset UpdatedDate { get; set; }

    public static NotificationDto From(SmsNotification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Type = n.Type.ToString(),
        Status = n.Status,
        ProviderMessageSid = n.ProviderMessageSid,
        ErrorCode = n.ErrorCode,
        ErrorMessage = n.ErrorMessage,
        IsScheduled = n.IsScheduled,
        ContentRedacted = n.ContentRedacted,
        ToPhoneNumberMasked = Mask(n.ToPhoneNumber),
        ResendOfNotificationId = n.ResendOfNotificationId,
        CreatedDate = n.CreatedDate,
        UpdatedDate = n.UpdatedDate
    };

    public static IReadOnlyList<NotificationDto> From(IEnumerable<SmsNotification> notifications)
    {
        var list = new List<NotificationDto>();
        foreach (var n in notifications) list.Add(From(n));
        return list;
    }

    private static string? Mask(string? number)
    {
        if (string.IsNullOrEmpty(number)) return null;
        if (number.Length <= 4) return new string('*', number.Length);
        return new string('*', number.Length - 4) + number[^4..];
    }
}
