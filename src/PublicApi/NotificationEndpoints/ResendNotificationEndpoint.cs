using System;
using System.Collections.Generic;
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
/// Operator action: re-sends a message that did not reach the shopper. The caller-supplied
/// idempotency key guarantees that repeating the request under the same key does not send
/// a second message; a fresh key is a genuine new attempt.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, IOrderNotificationService notificationService) =>
            {
                request.NotificationId = notificationId;
                return await HandleAsync(request, notificationService);
            })
            .Produces<ResendNotificationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, IOrderNotificationService notificationService)
    {
        var response = new ResendNotificationResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(response);
        }

        try
        {
            var (notification, idempotentReplay) = await notificationService.ResendAsync(request.NotificationId, request.IdempotencyKey);
            response.NotificationId = notification.Id;
            response.Status = notification.Status;
            response.IdempotentReplay = idempotentReplay;
            return Results.Ok(response);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound(response);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new { message = ex.Message });
        }
    }
}

public class ResendNotificationRequest : BaseRequest
{
    public int NotificationId { get; set; }

    /// <summary>Caller-supplied key; a repeat under the same key never sends twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }
    public ResendNotificationResponse() { }

    /// <summary>The identifier of the message the resend produced.</summary>
    public int NotificationId { get; set; }
    public string Status { get; set; } = string.Empty;

    /// <summary>True when the key was seen before and no new message was sent.</summary>
    public bool IdempotentReplay { get; set; }
}
