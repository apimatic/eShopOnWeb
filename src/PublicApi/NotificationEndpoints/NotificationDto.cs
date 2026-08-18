using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// The shape of a notification returned by the API. It carries its own <c>notificationId</c>
/// (what the operator endpoints act on), the provider's message identifier and the current
/// delivery outcome. The destination number is masked; the raw number is never exposed here.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string ToNumberMasked { get; set; } = string.Empty;
    public bool ScheduledFollowUp { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public bool ContentRedacted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StatusRefreshedAt { get; set; }

    public static NotificationDto FromEntity(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Kind = n.Kind.ToString(),
        Status = n.Status,
        ProviderMessageSid = n.ProviderMessageSid,
        ErrorCode = n.ErrorCode,
        ErrorMessage = n.ErrorMessage,
        ToNumberMasked = Mask(n.ToNumber),
        ScheduledFollowUp = n.IsScheduledFollowUp,
        ScheduledFor = n.ScheduledFor,
        ContentRedacted = n.ContentRedacted,
        CreatedAt = n.CreatedAt,
        StatusRefreshedAt = n.StatusRefreshedAt
    };

    private static string Mask(string number)
    {
        if (string.IsNullOrEmpty(number))
        {
            return string.Empty;
        }
        var last = number.Length <= 4 ? number : number[^4..];
        return "••••" + last;
    }
}
