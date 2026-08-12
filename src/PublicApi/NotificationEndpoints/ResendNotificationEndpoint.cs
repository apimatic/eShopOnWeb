using System;
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
    /// <summary>
    /// Caller-supplied idempotency key. Repeating a request under the same key must not send a second
    /// message; a genuine second attempt uses a fresh key. May also be supplied via the Idempotency-Key header.
    /// </summary>
    public string? IdempotencyKey { get; set; }
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }
    public ResendNotificationResponse() { }

    /// <summary>Identifier of the message the re-send produced (or, on a replay under the same key, the original re-send).</summary>
    public int NotificationId { get; set; }

    public string? ProviderMessageSid { get; set; }
    public string? Status { get; set; }
}

/// <summary>
/// POST /api/notifications/{notificationId}/resend — an operator re-sends a message that did not reach
/// the shopper, idempotent on a caller-supplied key. Restricted to administrators.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, HttpContext http, INotificationService service) =>
            {
                // The key may come in the body or the Idempotency-Key header.
                var idempotencyKey = request.IdempotencyKey;
                if (string.IsNullOrWhiteSpace(idempotencyKey) && http.Request.Headers.TryGetValue("Idempotency-Key", out var headerValue))
                {
                    idempotencyKey = headerValue.ToString();
                }

                if (string.IsNullOrWhiteSpace(idempotencyKey))
                {
                    return Results.BadRequest(new { error = "An idempotency key is required (body 'idempotencyKey' or 'Idempotency-Key' header)." });
                }

                var resend = await service.ResendAsync(notificationId, idempotencyKey);
                if (resend is null)
                {
                    return Results.NotFound();
                }

                var response = new ResendNotificationResponse(request.CorrelationId())
                {
                    NotificationId = resend.Id,
                    ProviderMessageSid = resend.ProviderMessageSid,
                    Status = resend.Status
                };
                return Results.Ok(response);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }
}
