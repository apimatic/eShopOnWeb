using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Messaging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// POST /api/notifications/{notificationId}/resend — operator re-sends a message that did not reach the shopper.
/// The caller-supplied idempotency key makes a repeat under the same key a no-op that returns the same result;
/// a genuine second attempt under a fresh key sends again. Restricted to the administrator role.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationBody, IOrderNotificationService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ResendNotificationEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationBody? body, IOrderNotificationService service) =>
                await HandleAsync(notificationId, body ?? new ResendNotificationBody(), service))
            .Produces<ResendNotificationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationBody request, IOrderNotificationService service)
        => await HandleAsync(0, request, service);

    private async Task<IResult> HandleAsync(int notificationId, ResendNotificationBody body, IOrderNotificationService service)
    {
        // The idempotency key may come from the body or the Idempotency-Key header.
        var key = body.IdempotencyKey;
        if (string.IsNullOrWhiteSpace(key))
            key = _httpContextAccessor.HttpContext?.Request.Headers["Idempotency-Key"].ToString();

        if (string.IsNullOrWhiteSpace(key))
            return Results.BadRequest(new { message = "An idempotency key is required (request body 'idempotencyKey' or 'Idempotency-Key' header)." });

        var result = await service.ResendAsync(notificationId, key!, CancellationToken.None);

        return Results.Ok(new ResendNotificationResponse
        {
            NotificationId = result.Notification.Id,
            DeliveryStatus = result.Notification.DeliveryStatus,
            WasReplay = result.WasReplay
        });
    }
}
