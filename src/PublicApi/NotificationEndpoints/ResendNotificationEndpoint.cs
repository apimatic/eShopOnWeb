using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationBody
{
    public string? IdempotencyKey { get; set; }
}

public class ResendNotificationRequest : BaseRequest
{
    public int NotificationId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }

    /// <summary>The identifier of the message the resend produced (a fresh one, or the earlier replay under the same key).</summary>
    public int NotificationId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? DeliveryStatus { get; set; }
}

/// <summary>
/// Operator action: re-sends a message that did not reach the shopper. The request carries a caller-supplied
/// idempotency key (JSON body <c>idempotencyKey</c> or the <c>Idempotency-Key</c> header): repeating under
/// the same key does not send a second message, while a fresh key is a legitimate second attempt.
/// Restricted to administrators.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, ISmsNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId,
             [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ResendNotificationBody? body,
             HttpContext http,
             ISmsNotificationService service) =>
            {
                var key = body?.IdempotencyKey;
                if (string.IsNullOrWhiteSpace(key) && http.Request.Headers.TryGetValue("Idempotency-Key", out var header))
                    key = header.ToString();

                return await HandleAsync(new ResendNotificationRequest
                {
                    NotificationId = notificationId,
                    IdempotencyKey = key ?? string.Empty
                }, service);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, ISmsNotificationService service)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return Results.BadRequest(new { message = "An idempotency key is required (JSON 'idempotencyKey' or the 'Idempotency-Key' header)." });

        var outcome = await service.ResendAsync(request.NotificationId, request.IdempotencyKey);

        switch (outcome.Code)
        {
            case ResendResultCode.Resent:
            case ResendResultCode.ReplayedIdempotent:
                return Results.Ok(new ResendNotificationResponse(request.CorrelationId())
                {
                    NotificationId = outcome.NotificationId!.Value,
                    Status = outcome.Code == ResendResultCode.Resent ? "resent" : "replayed",
                    DeliveryStatus = outcome.DeliveryStatus
                });
            case ResendResultCode.NotificationNotFound:
                return Results.NotFound(new { message = "Notification not found." });
            case ResendResultCode.ContentDisposed:
                return Results.Conflict(new { message = "The message content has been disposed of and cannot be re-sent." });
            case ResendResultCode.NumberRemoved:
                return Results.Conflict(new { message = "The destination number has been removed; nothing may be sent to it again." });
            default:
                return Results.Problem("Unexpected resend outcome.");
        }
    }
}
