using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// What was sent for an order and what became of it. Carries its own <c>notificationId</c> — the id
/// the operator endpoints act on — and the provider's message id and current delivery outcome.
/// The destination is masked; the auth token and the raw number are never exposed.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string DeliveryStatus { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? To { get; set; }
    public string? Body { get; set; }
    public bool IsFollowUp { get; set; }
    public DateTimeOffset? ScheduledForUtc { get; set; }
    public bool ContentDisposed { get; set; }
    public DateTimeOffset CreatedDate { get; set; }
    public DateTimeOffset UpdatedDate { get; set; }

    public static NotificationDto From(SmsNotification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Kind = n.Kind.ToString(),
        DeliveryStatus = n.DeliveryStatus,
        ProviderMessageSid = n.ProviderMessageSid,
        ErrorCode = n.ErrorCode,
        ErrorMessage = n.ErrorMessage,
        To = PhoneNumberMasking.Mask(n.ToNumber),
        Body = n.Body,
        IsFollowUp = n.IsFollowUp,
        ScheduledForUtc = n.ScheduledForUtc,
        ContentDisposed = n.ContentDisposed,
        CreatedDate = n.CreatedDate,
        UpdatedDate = n.UpdatedDate
    };
}
