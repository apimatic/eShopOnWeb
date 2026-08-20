using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string ProviderStatus { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public int? ProviderErrorCode { get; set; }
    public string? ProviderErrorMessage { get; set; }
    public string? Body { get; set; }
    public bool ContentRedacted { get; set; }
    public System.DateTimeOffset? ScheduledSendAt { get; set; }
    public System.DateTimeOffset CreatedAt { get; set; }
    public int? SourceNotificationId { get; set; }

    public static NotificationDto From(NotificationView view)
    {
        return new NotificationDto
        {
            NotificationId = view.NotificationId,
            OrderId = view.OrderId,
            Kind = view.Kind,
            ProviderStatus = view.ProviderStatus,
            ProviderMessageSid = view.ProviderMessageSid,
            ProviderErrorCode = view.ProviderErrorCode,
            ProviderErrorMessage = view.ProviderErrorMessage,
            Body = view.Body,
            ContentRedacted = view.ContentRedacted,
            ScheduledSendAt = view.ScheduledSendAt,
            CreatedAt = view.CreatedAt,
            SourceNotificationId = view.SourceNotificationId
        };
    }

    public static List<NotificationDto> From(IReadOnlyList<NotificationView> views)
    {
        var list = new List<NotificationDto>(views.Count);
        foreach (var view in views)
        {
            list.Add(From(view));
        }

        return list;
    }
}
