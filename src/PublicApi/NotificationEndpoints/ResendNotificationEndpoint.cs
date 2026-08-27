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

public class ResendNotificationRequest : BaseRequest
{
    public int NotificationId { get; set; }

    /// <summary>Caller-supplied key; repeating the request under the same key sends no second message.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationBody
{
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) {}
    public ResendNotificationResponse() {}

    /// <summary>The identifier of the message the resend produced.</summary>
    public int NotificationId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? MessageSid { get; set; }
    public bool AlreadyProcessed { get; set; }
}

/// <summary>
/// Operator action: re-sends a message that did not reach the shopper, idempotently
/// per caller-supplied key.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationBody body, IOrderNotificationService notificationService) =>
            {
                return await HandleAsync(new ResendNotificationRequest
                {
                    NotificationId = notificationId,
                    IdempotencyKey = body.IdempotencyKey
                }, notificationService);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, IOrderNotificationService notificationService)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "An idempotency key is required." });
        }

        var result = await notificationService.ResendAsync(request.NotificationId, request.IdempotencyKey);

        switch (result.Outcome)
        {
            case ResendOutcome.NotificationNotFound:
                return Results.NotFound();
            case ResendOutcome.ContentRedacted:
            case ResendOutcome.DestinationNoLongerRegistered:
                return Results.Conflict(new { message = result.Error });
            case ResendOutcome.AlreadyProcessed:
                return Results.Ok(new ResendNotificationResponse(request.CorrelationId())
                {
                    NotificationId = result.Notification!.Id,
                    Status = result.Notification.Status,
                    MessageSid = result.Notification.MessageSid,
                    AlreadyProcessed = true
                });
            default:
                return Results.Ok(new ResendNotificationResponse(request.CorrelationId())
                {
                    NotificationId = result.Notification!.Id,
                    Status = result.Notification.Status,
                    MessageSid = result.Notification.MessageSid
                });
        }
    }
}
