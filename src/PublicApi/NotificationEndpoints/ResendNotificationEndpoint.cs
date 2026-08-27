using System;
using System.ComponentModel.DataAnnotations;
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

    /// <summary>
    /// Caller-supplied idempotency key: repeating the request under the same key
    /// does not send a second message.
    /// </summary>
    [Required]
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }
    public ResendNotificationResponse() { }

    /// <summary>The identifier of the notification the resend produced.</summary>
    public int NotificationId { get; set; }
    public string? MessageSid { get; set; }
    public string Status { get; set; } = string.Empty;

    /// <summary>True when the idempotency key was already used and no new message was sent.</summary>
    public bool Duplicate { get; set; }
}

/// <summary>
/// Re-sends a message that did not reach the shopper (operator), idempotent on
/// the caller-supplied key.
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
            .Produces(StatusCodes.Status400BadRequest)
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

        return result.Outcome switch
        {
            ResendOutcome.NotFound => Results.NotFound(),
            ResendOutcome.ContentDisposed => Results.Conflict(new { message = "The message content has been disposed of and can no longer be sent." }),
            ResendOutcome.DestinationRemoved => Results.Conflict(new { message = "The destination number has been removed; nothing may be sent to it again." }),
            ResendOutcome.Duplicate => Results.Ok(ToResponse(request, result, duplicate: true)),
            _ => Results.Ok(ToResponse(request, result, duplicate: false))
        };
    }

    private static ResendNotificationResponse ToResponse(ResendNotificationRequest request, ResendNotificationResult result, bool duplicate) => new(request.CorrelationId())
    {
        NotificationId = result.Notification!.Id,
        MessageSid = result.Notification.MessageSid,
        Status = result.Notification.Status,
        Duplicate = duplicate
    };
}
