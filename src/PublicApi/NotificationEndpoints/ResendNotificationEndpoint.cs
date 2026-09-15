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
/// idempotency key makes a repeat under the same key a no-op (no second message), while a fresh key
/// is a genuine second attempt.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationEndpoint.Request, IOrderNotificationService>
{
    public record Request(int NotificationId, string IdempotencyKey);

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationBody? body, HttpContext http, IOrderNotificationService service) =>
            {
                var key = body?.IdempotencyKey;
                if (string.IsNullOrWhiteSpace(key) && http.Request.Headers.TryGetValue("Idempotency-Key", out var header))
                {
                    key = header.ToString();
                }

                if (string.IsNullOrWhiteSpace(key))
                {
                    return Results.BadRequest(new { message = "An idempotency key is required (body 'idempotencyKey' or 'Idempotency-Key' header)." });
                }

                return await HandleAsync(new Request(notificationId, key), service);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(Request request, IOrderNotificationService service)
    {
        var outcome = await service.ResendAsync(request.NotificationId, request.IdempotencyKey);
        if (outcome.NotFound || outcome.Notification is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new ResendNotificationResponse
        {
            NotificationId = outcome.Notification.Id,
            IdempotentReplay = outcome.IdempotentReplay
        });
    }
}
