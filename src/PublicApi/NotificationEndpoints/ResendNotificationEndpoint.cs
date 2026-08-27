using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: re-sends a message that did not reach the shopper. The caller
/// supplies an idempotency key; repeating the request under the same key does not send
/// a second message.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, HttpContext httpContext) =>
            {
                request.NotificationId = notificationId;
                return await HandleAsync(request, httpContext);
            })
            .Produces<ResendNotificationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, HttpContext httpContext)
    {
        var notificationRepository = httpContext.RequestServices.GetRequiredService<IRepository<OrderNotification>>();
        var notificationService = httpContext.RequestServices.GetRequiredService<IOrderNotificationService>();

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest("An idempotency key is required.");
        }

        var original = await notificationRepository.GetByIdAsync(request.NotificationId);
        if (original is null)
        {
            return Results.NotFound();
        }

        OrderNotification resend;
        try
        {
            resend = await notificationService.ResendAsync(original, request.IdempotencyKey, httpContext.RequestAborted);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(ex.Message);
        }

        var response = new ResendNotificationResponse(request.CorrelationId())
        {
            NotificationId = resend.Id,
            ProviderMessageSid = resend.ProviderMessageSid,
            ProviderStatus = resend.ProviderStatus,
            AcceptedByProvider = resend.AcceptedByProvider
        };
        return Results.Ok(response);
    }
}
