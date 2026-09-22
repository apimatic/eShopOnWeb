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
/// Operator action: re-sends a message that did not reach the shopper. The caller supplies an
/// idempotency key (body field <c>idempotencyKey</c> or the <c>Idempotency-Key</c> header): repeating a
/// request under the same key does not send a second message; a fresh key is a legitimate new attempt.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, ISmsNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ISmsNotificationService service, HttpContext http, ResendNotificationRequest? body) =>
            {
                var key = body?.IdempotencyKey;
                if (string.IsNullOrWhiteSpace(key) && http.Request.Headers.TryGetValue("Idempotency-Key", out var header))
                    key = header.ToString();

                if (string.IsNullOrWhiteSpace(key))
                    return Results.BadRequest(new { message = "An idempotency key is required (body 'idempotencyKey' or 'Idempotency-Key' header)." });

                return await HandleAsync(new ResendNotificationRequest { NotificationId = notificationId, IdempotencyKey = key }, service);
            })
            .Produces<ResendNotificationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, ISmsNotificationService service)
    {
        var outcome = await service.ResendAsync(request.NotificationId, request.IdempotencyKey!);
        return outcome.Status switch
        {
            ResendStatus.NotFound => Results.NotFound(),
            ResendStatus.NotEligible => Results.Conflict(new { message = "That message reached the shopper (or was not a failed send); it cannot be resent." }),
            ResendStatus.Replayed => Results.Ok(new ResendNotificationResponse(request.CorrelationId()) { NotificationId = outcome.NotificationId, Replayed = true }),
            _ => Results.Ok(new ResendNotificationResponse(request.CorrelationId()) { NotificationId = outcome.NotificationId, Replayed = false })
        };
    }
}

public class ResendNotificationRequest : BaseRequest
{
    public int NotificationId { get; set; }
    /// <summary>Caller-supplied idempotency key.</summary>
    public string? IdempotencyKey { get; set; }
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }
    public ResendNotificationResponse() { }

    /// <summary>Identifier of the message the resend produced (top-level).</summary>
    public int? NotificationId { get; set; }

    /// <summary>True when this request repeated a prior key and no new message was sent.</summary>
    public bool Replayed { get; set; }
}
