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
/// Operator action: re-sends a message that did not reach the shopper. A caller-supplied idempotency
/// key (the <c>Idempotency-Key</c> header) makes a repeat under the same key a no-op — no second
/// message — while a fresh key sends again. Returns the id of the message the resend produced.
/// </summary>
public class ResendNotificationEndpoint
    : IEndpoint<IResult, ResendNotificationRequest, IOrderNotificationService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
             IOrderNotificationService service, CancellationToken ct) =>
            {
                return await HandleAsync(new ResendNotificationRequest(notificationId, idempotencyKey), service, ct);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(
        ResendNotificationRequest request, IOrderNotificationService service, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "An Idempotency-Key header is required to resend a message." });
        }

        var notification = await service.ResendAsync(request.NotificationId, request.IdempotencyKey, ct);
        if (notification is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new ResendNotificationResponse(
            notification.Id, notification.ProviderMessageSid, notification.Status));
    }
}
