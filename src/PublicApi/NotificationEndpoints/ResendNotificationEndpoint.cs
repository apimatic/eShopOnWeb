using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Re-sends a message that did not reach the shopper (operator). The request carries a
/// caller-supplied idempotency key: a repeat under the same key does not send again.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IRepository<OrderNotification>, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, IRepository<OrderNotification> notificationRepository, IOrderNotificationService notificationService) =>
            {
                request.NotificationId = notificationId;
                return await HandleAsync(request, notificationRepository, notificationService);
            })
            .Produces<ResendNotificationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request,
        IRepository<OrderNotification> notificationRepository, IOrderNotificationService notificationService)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { error = "An idempotency key is required." });
        }

        var source = await notificationRepository.GetByIdAsync(request.NotificationId);
        if (source is null)
        {
            return Results.NotFound();
        }

        if (source.ContentRedacted || source.Body is null)
        {
            return Results.Conflict(new { error = "The content of this message has been disposed of and can no longer be sent." });
        }

        var result = await notificationService.ResendAsync(source, request.IdempotencyKey);

        var response = new ResendNotificationResponse(request.CorrelationId())
        {
            NotificationId = result.Notification.Id,
            ResendOfNotificationId = result.Notification.ResendOfNotificationId ?? source.Id,
            Status = result.Notification.Status,
            IdempotentReplay = result.IdempotentReplay
        };

        return Results.Ok(response);
    }
}
