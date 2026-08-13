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
/// Operator action: re-sends a message that did not reach the shopper. The caller-supplied
/// idempotency key makes a repeat under the same key a no-op (it returns the message the first
/// request produced), while a fresh key is a genuine second attempt.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, IOrderNotificationService notificationService) =>
            {
                request ??= new ResendNotificationRequest();
                request.NotificationId = notificationId;
                return await HandleAsync(request, notificationService);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, IOrderNotificationService notificationService)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return Results.BadRequest(new { error = "An idempotency key is required." });

        var result = await notificationService.ResendAsync(request.NotificationId, request.IdempotencyKey);

        return result.Outcome switch
        {
            ResendOutcome.NotFound => Results.NotFound(),
            ResendOutcome.Rejected => Results.Conflict(new { error = result.Error }),
            ResendOutcome.IdempotentReplay => Results.Ok(new ResendNotificationResponse(request.CorrelationId())
            {
                NotificationId = result.Notification!.Id,
                Status = result.Notification.Status,
                IdempotentReplay = true
            }),
            _ => Results.Created($"api/notifications/{result.Notification!.Id}", new ResendNotificationResponse(request.CorrelationId())
            {
                NotificationId = result.Notification!.Id,
                Status = result.Notification.Status,
                IdempotentReplay = false
            })
        };
    }
}

public class ResendNotificationRequest : BaseRequest
{
    internal int NotificationId { get; set; }

    /// <summary>Caller-supplied key that makes repeated resend requests idempotent.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }
    public ResendNotificationResponse() { }

    /// <summary>The identifier of the message this resend produced (or replayed).</summary>
    public int NotificationId { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IdempotentReplay { get; set; }
}
