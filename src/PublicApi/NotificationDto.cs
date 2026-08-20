using System;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi;

internal static class EndpointExceptionMapping
{
    public static IResult ToResult(this Exception exception)
    {
        return exception switch
        {
            InvalidContactNumberException invalid => Results.BadRequest(new { message = invalid.Message }),
            DuplicateException duplicate => Results.Conflict(new { message = duplicate.Message }),
            OrderNotificationException domain when domain.StatusCode == 404 => Results.NotFound(new { message = domain.Message }),
            OrderNotificationException domain when domain.StatusCode == 409 => Results.Conflict(new { message = domain.Message }),
            OrderNotificationException domain when domain.StatusCode == 401 => Results.Unauthorized(),
            OrderNotificationException domain => Results.Json(new { message = domain.Message }, statusCode: domain.StatusCode),
            _ => throw exception
        };
    }
}

public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? Body { get; set; }
    public bool ContentRedacted { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string? ProviderStatus { get; set; }
    public int? ProviderErrorCode { get; set; }
    public string? ProviderErrorMessage { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int? ResentFromNotificationId { get; set; }

    public static NotificationDto From(OrderNotification notification)
    {
        return new NotificationDto
        {
            NotificationId = notification.Id,
            OrderId = notification.OrderId,
            Kind = notification.Kind.ToString(),
            Body = notification.ContentRedacted ? null : notification.Body,
            ContentRedacted = notification.ContentRedacted,
            ProviderMessageSid = notification.ProviderMessageSid,
            ProviderStatus = notification.ProviderStatus,
            ProviderErrorCode = notification.ProviderErrorCode,
            ProviderErrorMessage = notification.ProviderErrorMessage,
            ScheduledSendAt = notification.ScheduledSendAt,
            CreatedAt = notification.CreatedAt,
            ResentFromNotificationId = notification.ResentFromNotificationId
        };
    }
}
