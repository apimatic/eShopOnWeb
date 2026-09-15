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

    /// <summary>Caller-supplied idempotency key. May also be provided via the <c>Idempotency-Key</c> header.</summary>
    public string? IdempotencyKey { get; set; }
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }
    public ResendNotificationResponse() { }

    /// <summary>The identifier of the message the resend produced (top-level).</summary>
    public int NotificationId { get; set; }
    public string DeliveryStatus { get; set; } = string.Empty;

    /// <summary>True when this request repeated a previously-seen idempotency key (no second message was sent).</summary>
    public bool Replayed { get; set; }
}

/// <summary>
/// POST /api/notifications/{notificationId}/resend — an operator re-sends a message that did not reach
/// the shopper. Repeating the request under the same idempotency key does not send a second message; a
/// fresh key is a genuine second attempt. Operator-only.
/// </summary>
public class ResendNotificationEndpoint : ApiEndpointBase,
    IEndpoint<IResult, ResendNotificationRequest, INotificationService>
{
    public ResendNotificationEndpoint(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor) { }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, INotificationService notificationService) =>
            {
                request.NotificationId = notificationId;
                return await HandleAsync(request, notificationService);
            })
            .Produces<ResendNotificationResponse>(StatusCodes.Status201Created)
            .Produces<ResendNotificationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, INotificationService notificationService)
    {
        var idempotencyKey = request.IdempotencyKey;
        if (string.IsNullOrWhiteSpace(idempotencyKey) &&
            HttpContext.Request.Headers.TryGetValue("Idempotency-Key", out var header))
        {
            idempotencyKey = header.ToString();
        }
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return Results.BadRequest(new { message = "An idempotency key is required (body 'idempotencyKey' or 'Idempotency-Key' header)." });

        var result = await notificationService.ResendAsync(request.NotificationId, idempotencyKey!, Aborted);
        switch (result.Outcome)
        {
            case ResendOutcome.OriginalNotFound:
                return Results.NotFound();
            case ResendOutcome.ContentDisposed:
                return Results.Conflict(new { message = "The message content was disposed of and can no longer be resent." });
            case ResendOutcome.ReplayedIdempotent:
                return Results.Ok(new ResendNotificationResponse(request.CorrelationId())
                {
                    NotificationId = result.Notification!.Id,
                    DeliveryStatus = result.Notification.DeliveryStatus,
                    Replayed = true
                });
            default:
                var response = new ResendNotificationResponse(request.CorrelationId())
                {
                    NotificationId = result.Notification!.Id,
                    DeliveryStatus = result.Notification.DeliveryStatus,
                    Replayed = false
                };
                return Results.Created($"api/notifications/{response.NotificationId}", response);
        }
    }
}
