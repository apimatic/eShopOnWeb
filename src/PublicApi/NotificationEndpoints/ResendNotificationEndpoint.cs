using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// POST /api/notifications/{notificationId}/resend — an operator re-sends a message that did not reach the
/// shopper. A caller-supplied <c>Idempotency-Key</c> header makes a repeat under the same key a no-op (it
/// returns the message the first call produced); a fresh key is a genuine new attempt. Administrator only.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int notificationId,
                HttpRequest httpRequest,
                IReadRepository<OrderNotification> repository,
                IOrderNotificationService notifications,
                CancellationToken ct) =>
            {
                var idempotencyKey = httpRequest.Headers[IdempotencyKeyHeader].ToString();
                if (string.IsNullOrWhiteSpace(idempotencyKey))
                {
                    return Results.BadRequest(new { message = $"An '{IdempotencyKeyHeader}' header is required." });
                }

                var original = await repository.GetByIdAsync(notificationId, ct);
                if (original is null)
                {
                    return Results.NotFound();
                }
                if (original.ContentRedacted)
                {
                    return Results.Conflict(new { message = "The message content has been disposed of and cannot be resent." });
                }

                var resend = await notifications.ResendAsync(notificationId, idempotencyKey, ct);
                return Results.Ok(new ResendNotificationResponse { NotificationId = resend.Id });
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("NotificationEndpoints");
    }
}
