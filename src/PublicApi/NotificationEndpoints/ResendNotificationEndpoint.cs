using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Re-sends a message that did not reach the shopper (operator). The request
/// carries a caller-supplied idempotency key: repeating the request under the
/// same key returns the message the first attempt produced, without sending
/// a second one.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequestBody body, ClaimsPrincipal user, IOrderNotificationService notificationService) =>
            {
                var request = new ResendNotificationRequest(notificationId)
                {
                    IdempotencyKey = body.IdempotencyKey,
                    OperatorId = user.Identity!.Name!
                };
                return await HandleAsync(request, notificationService);
            })
            .Produces<ResendNotificationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, IOrderNotificationService notificationService)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "An idempotency key is required." });
        }

        try
        {
            var notification = await notificationService.ResendAsync(request.NotificationId, request.IdempotencyKey, request.OperatorId);
            if (notification is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(new ResendNotificationResponse(request.CorrelationId())
            {
                NotificationId = notification.Id,
                OrderId = notification.OrderId,
                Status = notification.Status,
                ProviderMessageSid = notification.ProviderMessageSid
            });
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new { message = ex.Message });
        }
    }
}
