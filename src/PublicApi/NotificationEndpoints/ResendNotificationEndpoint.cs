using System.Threading;
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
/// idempotency key via the <c>Idempotency-Key</c> header — repeating a request under the same key does
/// not send a second message, while a fresh key is a legitimate second attempt.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, HttpContext http, IOrderNotificationService service, CancellationToken ct) =>
            {
                var idempotencyKey = http.Request.Headers["Idempotency-Key"].ToString();
                if (string.IsNullOrWhiteSpace(idempotencyKey))
                {
                    return Results.BadRequest(new { message = "An Idempotency-Key header is required." });
                }

                var result = await service.ResendAsync(notificationId, idempotencyKey, ct);
                if (!result.NotificationFound)
                {
                    return Results.NotFound();
                }

                return Results.Ok(new ResendResponse
                {
                    NotificationId = result.NotificationId!.Value,
                    Duplicate = result.WasDuplicate
                });
            })
            .Produces<ResendResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }
}
