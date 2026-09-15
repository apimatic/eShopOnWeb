using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// The view of a single notification returned by the API. Carries its own <see cref="NotificationId"/>
/// (what the operator endpoints act on) and the provider-owned state — the message SID and current
/// delivery outcome. The destination number is masked; it is never returned in full.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public string DeliveryStatus { get; set; } = string.Empty;
    public int? ErrorCode { get; set; }
    public bool ContentRedacted { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public static NotificationDto From(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Type = n.Type.ToString(),
        To = PhoneMask.Mask(n.ToPhoneNumber),
        ProviderMessageSid = n.ProviderMessageSid,
        DeliveryStatus = n.DeliveryStatus,
        ErrorCode = n.ErrorCode,
        ContentRedacted = n.ContentRedacted,
        ScheduledSendAt = n.ScheduledSendAt,
        CreatedAt = n.CreatedAt,
        UpdatedAt = n.UpdatedAt
    };
}

/// <summary>Masks a phone number to its last four digits so it is never surfaced in full.</summary>
public static class PhoneMask
{
    public static string Mask(string? phoneNumber)
    {
        if (string.IsNullOrEmpty(phoneNumber))
            return string.Empty;

        var digitsShown = 4;
        if (phoneNumber.Length <= digitsShown)
            return new string('*', phoneNumber.Length);

        var last4 = phoneNumber[^digitsShown..];
        return $"{new string('*', phoneNumber.Length - digitsShown)}{last4}";
    }
}
