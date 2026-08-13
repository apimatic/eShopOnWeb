using System;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest
{
    /// <summary>The caller-supplied idempotency key (may also be sent as the Idempotency-Key header).</summary>
    public string? IdempotencyKey { get; set; }
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }
    public ResendNotificationResponse() { }

    /// <summary>The id of the message the resend produced (the same one on a repeat under the same key).</summary>
    public int NotificationId { get; set; }

    public string? ProviderSid { get; set; }
    public string? Status { get; set; }
}

/// <summary>
/// An operator re-sends a message that did not reach the shopper. The request carries an idempotency
/// key (header <c>Idempotency-Key</c> or body field): repeating under the same key does not send a
/// second message, while a fresh key is a genuine new attempt. Administrator role only.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, HttpContext http, ISmsNotificationService service) =>
            {
                // Prefer the Idempotency-Key header; fall back to a JSON body { idempotencyKey } if present.
                var key = http.Request.Headers[IdempotencyKeyHeader].ToString();
                if (string.IsNullOrWhiteSpace(key) && (http.Request.ContentLength ?? 0) > 0)
                {
                    try
                    {
                        var body = await http.Request.ReadFromJsonAsync<ResendNotificationRequest>();
                        key = body?.IdempotencyKey ?? string.Empty;
                    }
                    catch { /* not a JSON body; treat as no key supplied */ }
                }
                if (string.IsNullOrWhiteSpace(key))
                    return Results.BadRequest($"An idempotency key is required (header '{IdempotencyKeyHeader}' or body 'idempotencyKey').");

                // A missing notification throws NotificationNotFoundException, mapped to 404 by the middleware.
                var resend = await service.ResendAsync(notificationId, key);

                return Results.Ok(new ResendNotificationResponse
                {
                    NotificationId = resend.Id,
                    ProviderSid = resend.ProviderSid,
                    Status = resend.ProviderStatus
                });
            })
            .Produces<ResendNotificationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }
}
