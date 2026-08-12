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

public class ResendNotificationBody
{
    /// <summary>Caller-supplied idempotency key. May also be supplied via the Idempotency-Key header.</summary>
    public string? IdempotencyKey { get; set; }
}

public class ResendNotificationRequest : BaseRequest
{
    public int NotificationId { get; set; }
    public string? IdempotencyKey { get; set; }
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }
    public ResendNotificationResponse() { }

    /// <summary>The identifier of the message this resend produced (or replayed).</summary>
    public int NotificationId { get; set; }
    public string? Status { get; set; }
    public string Outcome { get; set; } = string.Empty;
}

/// <summary>
/// Operator action: re-sends a message that did not reach the shopper. Repeating the request under the
/// same idempotency key does not send a second message; a fresh key is a genuine second attempt.
/// Restricted to administrators.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, INotificationAdminService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, INotificationAdminService service, HttpContext http, ResendNotificationBody? body) =>
            {
                var key = body?.IdempotencyKey;
                if (string.IsNullOrWhiteSpace(key) && http.Request.Headers.TryGetValue("Idempotency-Key", out var header))
                {
                    key = header.ToString();
                }

                var request = new ResendNotificationRequest { NotificationId = notificationId, IdempotencyKey = key };
                return await HandleAsync(request, service);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, INotificationAdminService service)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { error = "An idempotency key is required (body field 'idempotencyKey' or 'Idempotency-Key' header)." });
        }

        var result = await service.ResendAsync(request.NotificationId, request.IdempotencyKey!);
        return result.Outcome switch
        {
            ResendOutcome.NotFound => Results.NotFound(new { error = result.Error }),
            ResendOutcome.Invalid => Results.BadRequest(new { error = result.Error }),
            _ => Results.Ok(new ResendNotificationResponse(request.CorrelationId())
            {
                NotificationId = result.NotificationId,
                Status = result.Status,
                Outcome = result.Outcome == ResendOutcome.ReplayedIdempotent ? "replayed" : "sent"
            })
        };
    }
}
