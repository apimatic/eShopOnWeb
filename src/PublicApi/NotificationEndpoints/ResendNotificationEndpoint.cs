using System;
using System.Threading;
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
    /// <summary>Caller-supplied idempotency key: repeating the same key never sends twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }
    public ResendNotificationResponse() { }

    /// <summary>Identifier of the message the resend produced.</summary>
    public int NotificationId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? MessageSid { get; set; }
    public bool IdempotentReplay { get; set; }
}

/// <summary>
/// Operator action: re-sends a message that did not reach the shopper.
/// Idempotent on the caller-supplied key.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request,
             IOrderNotificationService notificationService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(notificationId, request, notificationService, cancellationToken);
            })
            .Produces<ResendNotificationResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, ResendNotificationRequest request,
        IOrderNotificationService notificationService, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { error = "idempotencyKey is required." });
        }

        var result = await notificationService.ResendAsync(notificationId, request.IdempotencyKey, cancellationToken);
        if (!result.Success || result.Notification == null)
        {
            return result.Error == "Notification not found."
                ? Results.NotFound()
                : Results.Conflict(new { error = result.Error });
        }

        var response = new ResendNotificationResponse(request.CorrelationId())
        {
            NotificationId = result.Notification.Id,
            Status = result.Notification.Status,
            MessageSid = result.Notification.MessageSid,
            IdempotentReplay = result.WasIdempotentReplay
        };
        return Results.Created($"api/notifications/{result.Notification.Id}", response);
    }
}
