using System.Threading;
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
/// Operator action: re-sends a message that did not reach the shopper. The request carries a caller-supplied
/// idempotency key (in the body or the <c>Idempotency-Key</c> header); repeating a request under the same key
/// sends nothing new, while a fresh key is a genuine new attempt. Restricted to the administrator role.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int notificationId,
                ResendNotificationRequest? request,
                HttpContext http,
                IOrderNotificationService notifications,
                CancellationToken ct) =>
            {
                var idempotencyKey = request?.IdempotencyKey;
                if (string.IsNullOrWhiteSpace(idempotencyKey))
                    idempotencyKey = http.Request.Headers["Idempotency-Key"].ToString();

                if (string.IsNullOrWhiteSpace(idempotencyKey))
                    return Results.BadRequest(new { message = "An idempotency key is required (request body or Idempotency-Key header)." });

                var outcome = await notifications.ResendAsync(notificationId, idempotencyKey, ct);
                if (outcome is null)
                    return Results.NotFound();

                return Results.Ok(new ResendNotificationResponse(outcome.NotificationId, outcome.AlreadyProcessed));
            })
            .Produces<ResendNotificationResponse>()
            .WithTags("NotificationEndpoints");
    }
}

public record ResendNotificationRequest(string IdempotencyKey);

/// <summary><c>notificationId</c> is the identifier of the message the re-send produced.</summary>
public record ResendNotificationResponse(int NotificationId, bool AlreadyProcessed);
