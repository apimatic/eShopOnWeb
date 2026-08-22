using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Ardalis.Result;
using Microsoft.AspNetCore.Http;
using HttpResult = Microsoft.AspNetCore.Http.IResult;

namespace Microsoft.eShopWeb.PublicApi;

internal static class EndpointHelpers
{
    public static string? GetBuyerId(this ClaimsPrincipal user) => user.Identity?.Name;

    public static bool IsAdministrator(this ClaimsPrincipal user) =>
        user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);

    public static HttpResult ToHttpResult(this Result result)
    {
        return result.Status switch
        {
            ResultStatus.Ok => Results.Ok(),
            ResultStatus.NotFound => Results.NotFound(),
            ResultStatus.Unauthorized => Results.Unauthorized(),
            ResultStatus.Forbidden => Results.Forbid(),
            ResultStatus.Invalid => Results.BadRequest(new { errors = result.ValidationErrors.Select(e => e.ErrorMessage) }),
            ResultStatus.Error => Results.Json(new { errors = result.Errors }, statusCode: StatusCodes.Status502BadGateway),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    public static HttpResult ToHttpResult<T>(this Result<T> result, Func<T, HttpResult> onSuccess)
    {
        return result.Status switch
        {
            ResultStatus.Ok => onSuccess(result.Value),
            ResultStatus.NotFound => Results.NotFound(),
            ResultStatus.Unauthorized => Results.Unauthorized(),
            ResultStatus.Forbidden => Results.Forbid(),
            ResultStatus.Invalid => Results.BadRequest(new { errors = result.ValidationErrors.Select(e => e.ErrorMessage) }),
            ResultStatus.Error => Results.Json(new { errors = result.Errors }, statusCode: StatusCodes.Status502BadGateway),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    public static NotificationDto ToDto(this ApplicationCore.Entities.NotificationAggregate.OrderNotification notification)
    {
        return new NotificationDto
        {
            NotificationId = notification.Id,
            OrderId = notification.OrderId,
            Kind = notification.Kind.ToString(),
            Body = notification.ContentRedacted ? string.Empty : notification.Body,
            ContentRedacted = notification.ContentRedacted,
            ProviderMessageSid = notification.ProviderMessageSid,
            ProviderStatus = notification.ProviderStatus,
            ProviderErrorCode = notification.ProviderErrorCode,
            ProviderErrorMessage = notification.ProviderErrorMessage,
            CreatedAt = notification.CreatedAt,
            ScheduledFor = notification.ScheduledFor,
            ProviderDateSent = notification.ProviderDateSent,
            ResentFromNotificationId = notification.ResentFromNotificationId
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
    public string ProviderStatus { get; set; } = string.Empty;
    public int? ProviderErrorCode { get; set; }
    public string? ProviderErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public DateTimeOffset? ProviderDateSent { get; set; }
    public int? ResentFromNotificationId { get; set; }
}
