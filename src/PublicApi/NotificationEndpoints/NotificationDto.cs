using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// A notification as returned by the API. It carries the operator's handle (<see cref="NotificationId"/>),
/// the provider's identifier and current delivery outcome, and a masked destination — the full number
/// is never echoed back.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string DeliveryStatus { get; set; } = string.Empty;
    public string? ProviderMessageId { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public bool ContentDisposed { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public string ToPhoneNumberMasked { get; set; } = string.Empty;

    public static NotificationDto From(Notification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Kind = n.Kind.ToString(),
        DeliveryStatus = n.DeliveryStatus,
        ProviderMessageId = n.ProviderMessageId,
        ErrorCode = n.ErrorCode,
        ErrorMessage = n.ErrorMessage,
        ContentDisposed = n.ContentDisposed,
        CreatedAt = n.CreatedAt,
        ScheduledFor = n.ScheduledFor,
        ToPhoneNumberMasked = MaskNumber(n.ToPhoneNumber)
    };

    /// <summary>Shows only the last four digits — enough to recognise a number, not to read it back.</summary>
    public static string MaskNumber(string number)
    {
        if (string.IsNullOrEmpty(number))
            return string.Empty;
        if (number.Length <= 4)
            return new string('*', number.Length);
        return new string('*', number.Length - 4) + number[^4..];
    }
}
