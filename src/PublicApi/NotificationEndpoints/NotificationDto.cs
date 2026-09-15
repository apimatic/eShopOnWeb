using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// The caller-facing view of a notification: enough of the provider-owned state (its identifier and
/// current delivery outcome) that an operator endpoint can act on it and report on it. Deliberately
/// omits the destination number and the message body.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string DeliveryStatus { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public int? ProviderErrorCode { get; set; }
    public string? ProviderErrorMessage { get; set; }
    public bool ContentDisposed { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }
    public DateTimeOffset CreatedDate { get; set; }
    public DateTimeOffset? StatusUpdatedDate { get; set; }

    public static NotificationDto From(Notification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Kind = n.Kind.ToString(),
        DeliveryStatus = n.DeliveryStatus,
        ProviderMessageSid = n.ProviderMessageSid,
        ProviderErrorCode = n.ProviderErrorCode,
        ProviderErrorMessage = n.ProviderErrorMessage,
        ContentDisposed = n.ContentDisposed,
        ScheduledSendAt = n.ScheduledSendAt,
        CreatedDate = n.CreatedDate,
        StatusUpdatedDate = n.StatusUpdatedDate
    };
}
