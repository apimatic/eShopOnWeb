using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: re-sends a message that did not reach the shopper. The
/// caller-supplied idempotency key makes a repeated request safe — a repeat
/// under the same key returns the resend it already produced.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, HttpContext>
{
    private readonly IOrderNotificationService _notifications;

    public ResendNotificationEndpoint(IOrderNotificationService notifications)
    {
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequestBody body, HttpContext httpContext) =>
            {
                return await HandleAsync(new ResendNotificationRequest(notificationId, body?.IdempotencyKey ?? string.Empty), httpContext);
            })
            .Produces<ResendNotificationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, HttpContext httpContext)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "An idempotency key is required." });
        }

        var result = await _notifications.ResendAsync(request.NotificationId, request.IdempotencyKey, httpContext.RequestAborted);

        return result.Outcome switch
        {
            ResendOutcome.Resent => Results.Ok(new ResendNotificationResponse(request.CorrelationId())
            {
                NotificationId = result.Notification!.Id,
                Status = result.Notification.Status
            }),
            ResendOutcome.DuplicateIdempotencyKey => Results.Ok(new ResendNotificationResponse(request.CorrelationId())
            {
                NotificationId = result.Notification!.Id,
                Status = result.Notification.Status,
                AlreadyExisted = true
            }),
            ResendOutcome.NotificationNotFound => Results.NotFound(),
            ResendOutcome.ContactNumberRemoved => Results.Conflict(new { message = result.ErrorMessage }),
            ResendOutcome.ContentRedacted => Results.Conflict(new { message = result.ErrorMessage }),
            _ => Results.Problem(result.ErrorMessage ?? "The message could not be re-sent.", statusCode: StatusCodes.Status502BadGateway)
        };
    }
}

public class ResendNotificationRequestBody
{
    public string? IdempotencyKey { get; set; }
}

public class ResendNotificationRequest : BaseRequest
{
    public ResendNotificationRequest(int notificationId, string idempotencyKey)
    {
        NotificationId = notificationId;
        IdempotencyKey = idempotencyKey;
    }

    public int NotificationId { get; }
    public string IdempotencyKey { get; }
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }

    /// <summary>The identifier of the message the resend produced.</summary>
    public int NotificationId { get; set; }
    public string Status { get; set; } = string.Empty;

    /// <summary>True when this key already produced a resend and nothing new was sent.</summary>
    public bool AlreadyExisted { get; set; }
}
