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
/// Operator action: re-sends a message that did not reach the shopper. The caller-supplied
/// idempotency key makes a repeated request harmless while a fresh key is a genuine new attempt.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint
{
    private const string IdempotencyHeader = "Idempotency-Key";

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int notificationId,
                ResendNotificationRequest? request,
                HttpContext http,
                IOrderNotificationService notificationService,
                CancellationToken cancellationToken) =>
            {
                var idempotencyKey = request?.IdempotencyKey;
                if (string.IsNullOrWhiteSpace(idempotencyKey) &&
                    http.Request.Headers.TryGetValue(IdempotencyHeader, out var headerValue))
                {
                    idempotencyKey = headerValue.ToString();
                }

                if (string.IsNullOrWhiteSpace(idempotencyKey))
                {
                    return Results.BadRequest(new { error = "An idempotency key is required (body 'idempotencyKey' or 'Idempotency-Key' header)." });
                }

                var result = await notificationService.ResendAsync(notificationId, idempotencyKey!, cancellationToken);
                if (!result.Succeeded || result.NotificationId is null)
                {
                    if (result.Error == "Notification not found.")
                    {
                        return Results.NotFound();
                    }
                    return Results.Conflict(new { error = result.Error });
                }

                var response = new ResendNotificationResponse
                {
                    NotificationId = result.NotificationId.Value,
                    Duplicate = result.Duplicate
                };
                return Results.Ok(response);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }
}
