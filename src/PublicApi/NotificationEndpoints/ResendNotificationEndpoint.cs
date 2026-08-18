using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendRequest
{
    /// <summary>Caller-supplied idempotency key. A repeat under the same key must not send a second message.</summary>
    public string? IdempotencyKey { get; set; }

    [JsonIgnore] public int NotificationId { get; set; }
    [JsonIgnore] public CancellationToken Ct { get; set; }
}

/// <summary>
/// Operator action: re-send a message that did not reach the shopper. Repeating under the same
/// idempotency key returns the same result and sends nothing new; a fresh key is a genuine new
/// attempt. Returns the <c>notificationId</c> of the message the resend produced.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendRequest? body, [FromHeader(Name = "Idempotency-Key")] string? headerKey, IOrderNotificationService service, CancellationToken ct) =>
            {
                var request = body ?? new ResendRequest();
                request.NotificationId = notificationId;
                request.Ct = ct;
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                {
                    request.IdempotencyKey = headerKey;
                }

                return await HandleAsync(request, service);
            })
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendRequest request, IOrderNotificationService service)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "An idempotency key is required (body 'idempotencyKey' or 'Idempotency-Key' header)." });
        }

        var outcome = await service.ResendAsync(request.NotificationId, request.IdempotencyKey!, request.Ct);
        return outcome.Status switch
        {
            ResendStatus.SourceNotFound => Results.NotFound(),
            ResendStatus.ContentUnavailable => Results.Conflict(new { message = "The message content was disposed of; there is nothing to re-send." }),
            _ => Results.Ok(new { notificationId = outcome.NotificationId, status = outcome.Status.ToString() })
        };
    }
}
