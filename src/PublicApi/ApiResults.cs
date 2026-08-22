using System;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi;

internal static class ApiResults
{
    public static IResult From(int statusCode, object? body = null, string? error = null)
    {
        return statusCode switch
        {
            StatusCodes.Status200OK => Results.Ok(body),
            StatusCodes.Status201Created => Results.Json(body, statusCode: StatusCodes.Status201Created),
            StatusCodes.Status204NoContent => Results.NoContent(),
            StatusCodes.Status400BadRequest => Results.BadRequest(new ErrorBody(error ?? "Bad request.")),
            StatusCodes.Status404NotFound => Results.NotFound(new ErrorBody(error ?? "Not found.")),
            StatusCodes.Status409Conflict => Results.Conflict(new ErrorBody(error ?? "Conflict.")),
            _ => Results.Json(new ErrorBody(error ?? "Request failed."), statusCode: statusCode)
        };
    }
}

internal sealed class ErrorBody
{
    public ErrorBody(string error) => Error = error;
    public string Error { get; }
}

internal static class NotificationResponseMapper
{
    public static NotificationDto ToDto(OrderNotification notification)
    {
        return new NotificationDto
        {
            NotificationId = notification.Id,
            OrderId = notification.OrderId,
            Kind = notification.Kind.ToString(),
            Status = notification.Status,
            ProviderMessageSid = notification.ProviderMessageSid,
            ProviderErrorCode = notification.ProviderErrorCode,
            ProviderErrorMessage = notification.ProviderErrorMessage,
            Body = notification.ContentRedacted ? null : notification.Body,
            ContentRedacted = notification.ContentRedacted,
            ScheduledFor = notification.ScheduledFor,
            CreatedAt = notification.CreatedAt,
            OriginalNotificationId = notification.OriginalNotificationId
        };
    }

    public static OrderSummaryDto ToOrderDto(Order order)
    {
        return new OrderSummaryDto
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            OrderDate = order.OrderDate,
            Total = order.Total()
        };
    }
}

public sealed class NotificationDto
{
    public int NotificationId { get; init; }
    public int OrderId { get; init; }
    public string Kind { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? ProviderMessageSid { get; init; }
    public int? ProviderErrorCode { get; init; }
    public string? ProviderErrorMessage { get; init; }
    public string? Body { get; init; }
    public bool ContentRedacted { get; init; }
    public DateTimeOffset? ScheduledFor { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public int? OriginalNotificationId { get; init; }
}

public sealed class OrderSummaryDto
{
    public int OrderId { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset OrderDate { get; init; }
    public decimal Total { get; init; }
}
