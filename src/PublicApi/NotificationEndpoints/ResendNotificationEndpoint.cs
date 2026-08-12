using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: re-sends a message that did not reach the shopper. The request carries a
/// caller-supplied idempotency key (the <c>Idempotency-Key</c> header, or an <c>idempotencyKey</c>
/// query value). Repeating under the same key returns the notification the first attempt produced
/// without sending a second message; a fresh key is a legitimate new attempt.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationEndpoint.Request, IOrderNotificationService>
{
    public record Request(int NotificationId, string? IdempotencyKey);

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId,
             [FromHeader(Name = "Idempotency-Key")] string? idempotencyKeyHeader,
             [FromQuery] string? idempotencyKey,
             IOrderNotificationService notifications) =>
            {
                var key = !string.IsNullOrWhiteSpace(idempotencyKeyHeader) ? idempotencyKeyHeader : idempotencyKey;
                return await HandleAsync(new Request(notificationId, key), notifications);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(Request request, IOrderNotificationService notifications)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { error = "An idempotency key is required (Idempotency-Key header or idempotencyKey query value)." });
        }

        var result = await notifications.ResendAsync(request.NotificationId, request.IdempotencyKey!, CancellationToken.None);
        if (result is null)
        {
            return Results.NotFound();
        }

        var notification = result.Notification;
        var response = new ResendNotificationResponse
        {
            NotificationId = notification.Id,
            ResendOfNotificationId = notification.ResendOfNotificationId,
            Status = notification.Status.ToString(),
            ProviderMessageSid = notification.ProviderMessageSid,
            Replayed = result.Replayed
        };
        return Results.Ok(response);
    }
}
