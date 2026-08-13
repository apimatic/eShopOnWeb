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
/// Operator action: re-sends a message that did not reach the shopper. The caller supplies an idempotency
/// key (the <c>Idempotency-Key</c> header, or an <c>idempotencyKey</c> query value): repeating a request
/// under the same key does not send a second message, while a fresh key is a legitimate new attempt.
/// Returns the identifier of the message the resend produced. Restricted to administrators.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int notificationId,
                HttpContext http,
                IOrderNotificationService notificationService,
                CancellationToken cancellationToken) =>
            {
                var idempotencyKey = ReadIdempotencyKey(http);
                if (string.IsNullOrWhiteSpace(idempotencyKey))
                {
                    return Results.BadRequest(new { message = "An idempotency key is required (Idempotency-Key header or idempotencyKey query value)." });
                }

                // NotFound / content-disposed cases surface as domain exceptions mapped by the exception middleware.
                var resend = await notificationService.ResendAsync(notificationId, idempotencyKey!, cancellationToken);
                return Results.Ok(new { notificationId = resend.Id, status = resend.Status });
            })
            .WithTags("NotificationEndpoints");
    }

    private static string? ReadIdempotencyKey(HttpContext http)
    {
        if (http.Request.Headers.TryGetValue("Idempotency-Key", out var header) && !string.IsNullOrWhiteSpace(header))
        {
            return header.ToString();
        }
        if (http.Request.Query.TryGetValue("idempotencyKey", out var query) && !string.IsNullOrWhiteSpace(query))
        {
            return query.ToString();
        }
        return null;
    }
}
