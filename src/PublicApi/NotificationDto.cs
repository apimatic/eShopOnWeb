using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi;

public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? Body { get; set; }
    public bool ContentDisposed { get; set; }
    public string? DestinationNumber { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string? ProviderStatus { get; set; }
    public int? ProviderErrorCode { get; set; }
    public string? ProviderErrorMessage { get; set; }
    public DateTimeOffset? ProviderDateSent { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int? ResendOfNotificationId { get; set; }
    public string? SendFailureReason { get; set; }

    public static NotificationDto From(OrderNotification notification) => new()
    {
        NotificationId = notification.Id,
        OrderId = notification.OrderId,
        Kind = notification.Kind.ToString(),
        Body = notification.RetrievableBody(),
        ContentDisposed = notification.ContentDisposed
            || (string.IsNullOrWhiteSpace(notification.RetrievableBody())
                && !string.IsNullOrEmpty(notification.ProviderMessageSid)
                && notification.IsTerminalProviderStatus()),
        DestinationNumber = notification.DestinationNumber,
        ProviderMessageSid = notification.ProviderMessageSid,
        ProviderStatus = notification.ProviderStatus,
        ProviderErrorCode = notification.ProviderErrorCode,
        ProviderErrorMessage = notification.ProviderErrorMessage,
        ProviderDateSent = notification.ProviderDateSent,
        ScheduledFor = notification.ScheduledFor,
        CreatedAt = notification.CreatedAt,
        ResendOfNotificationId = notification.ResendOfNotificationId,
        SendFailureReason = notification.SendFailureReason
    };
}
