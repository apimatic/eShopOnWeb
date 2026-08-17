using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SmsNotificationEndpoints;

/// <summary>
/// POST /api/notifications/{notificationId}/resend — an operator re-sends a message that did not reach the
/// shopper. A caller-supplied idempotency key (body or <c>Idempotency-Key</c> header) makes a repeat under
/// the same key a no-op; a fresh key is a genuine new attempt. Administrator only.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int notificationId,
                ResendNotificationRequest? request,
                HttpContext httpContext,
                IOrderNotificationService orderNotificationService,
                CancellationToken cancellationToken) =>
            {
                var idempotencyKey = request?.IdempotencyKey;
                if (string.IsNullOrWhiteSpace(idempotencyKey) && httpContext.Request.Headers.TryGetValue(IdempotencyKeyHeader, out var header))
                {
                    idempotencyKey = header.ToString();
                }

                if (string.IsNullOrWhiteSpace(idempotencyKey))
                {
                    return Results.BadRequest(new { error = "An idempotency key is required (request body 'idempotencyKey' or the 'Idempotency-Key' header)." });
                }

                var result = await orderNotificationService.ResendAsync(notificationId, idempotencyKey.Trim(), cancellationToken);
                if (!result.OriginalFound)
                {
                    return Results.NotFound();
                }
                if (result.Error is not null || result.NotificationId is null)
                {
                    return Results.BadRequest(new { error = result.Error });
                }

                return Results.Ok(new ResendNotificationResponse
                {
                    NotificationId = result.NotificationId.Value,
                    Reused = result.Reused
                });
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }
}
