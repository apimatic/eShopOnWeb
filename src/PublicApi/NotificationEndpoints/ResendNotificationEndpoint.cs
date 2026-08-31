using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Re-sends a message that did not reach the shopper (operator). The caller-supplied
/// idempotency key makes a repeated request safe: the same key never sends twice.
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

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, IRepository<OrderNotification> notificationRepository, IOrderNotificationService notificationService)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { error = "idempotencyKey is required." });
        }

        var source = await notificationRepository.GetByIdAsync(request.NotificationId);
        if (source == null)
        {
            return Results.NotFound();
        }

        var alreadyProcessed = await notificationRepository.FirstOrDefaultAsync(
            new OrderNotificationByIdempotencyKeySpecification(request.IdempotencyKey));
        if (alreadyProcessed != null)
        {
            return Results.Ok(new ResendNotificationResponse(request.CorrelationId())
            {
                NotificationId = alreadyProcessed.Id,
                Status = alreadyProcessed.LastKnownStatus,
                IdempotentReplay = true
            });
        }

        if (source.ContentRedacted || source.Body == null)
        {
            return Results.Conflict(new { error = "The message content has been disposed of and can no longer be sent." });
        }

        await notificationService.RefreshStatusAsync(source);
        if (source.LastKnownStatus == MessageStatuses.Delivered)
        {
            return Results.Conflict(new { error = "The message was already delivered to the shopper." });
        }

        try
        {
            var resend = await notificationService.ResendAsync(source, request.IdempotencyKey);
            return Results.Ok(new ResendNotificationResponse(request.CorrelationId())
            {
                NotificationId = resend.Id,
                Status = resend.LastKnownStatus,
                IdempotentReplay = false
            });
        }
        catch (SmsProviderException ex)
        {
            return ProviderErrorResults.Map(ex);
        }
    }
}
