using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Sms;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest
{
    // Caller-supplied idempotency key. May also be supplied via the "Idempotency-Key" header.
    public string? IdempotencyKey { get; set; }
}

public class ResendNotificationResponse
{
    // Top-level identifier of the message the resend produced (the same one on an idempotent replay).
    public int NotificationId { get; set; }
    public string Status { get; set; } = string.Empty;

    // True when a prior request under the same key already produced this message and nothing was re-sent.
    public bool Replayed { get; set; }
}

/// <summary>
/// Operator action: re-sends a message that did not reach the shopper. Repeating the request under the
/// same idempotency key does not send a second message; a fresh key is a legitimate new attempt.
/// Restricted to administrators.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest? request, HttpContext http,
                INotificationService notificationService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(notificationId, request, http, notificationService, cancellationToken);
            })
            .Produces<ResendNotificationResponse>(StatusCodes.Status201Created)
            .Produces<ResendNotificationResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, ResendNotificationRequest? request, HttpContext http,
        INotificationService notificationService, CancellationToken cancellationToken)
    {
        var idempotencyKey = request?.IdempotencyKey;
        if (string.IsNullOrWhiteSpace(idempotencyKey) &&
            http.Request.Headers.TryGetValue("Idempotency-Key", out var headerValue))
        {
            idempotencyKey = headerValue.ToString();
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Results.BadRequest(new { message = "An idempotency key is required (body 'idempotencyKey' or 'Idempotency-Key' header)." });
        }

        var result = await notificationService.ResendAsync(notificationId, idempotencyKey!, cancellationToken);

        switch (result.Outcome)
        {
            case ResendOutcome.NotFound:
                return Results.NotFound();
            case ResendOutcome.ContactRemoved:
                return Results.Conflict(new { message = "The target contact number has been removed; nothing may be sent to it again." });
            case ResendOutcome.ContentDisposed:
                return Results.Conflict(new { message = "The message content was disposed of and can no longer be re-sent." });
            case ResendOutcome.Replayed:
                return Results.Ok(new ResendNotificationResponse
                {
                    NotificationId = result.Notification!.Id,
                    Status = result.Notification.Status,
                    Replayed = true
                });
            case ResendOutcome.Created:
            default:
                var response = new ResendNotificationResponse
                {
                    NotificationId = result.Notification!.Id,
                    Status = result.Notification.Status,
                    Replayed = false
                };
                return Results.Created($"api/notifications/{response.NotificationId}", response);
        }
    }
}
