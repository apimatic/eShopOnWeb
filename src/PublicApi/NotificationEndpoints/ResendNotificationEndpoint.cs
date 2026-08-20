using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRouteRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, IOrderNotificationService service) =>
            {
                return await HandleAsync(
                    new ResendNotificationRouteRequest(notificationId, request?.IdempotencyKey ?? string.Empty),
                    service);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRouteRequest request, IOrderNotificationService service)
    {
        try
        {
            var notification = await service.ResendAsync(request.NotificationId, request.IdempotencyKey);
            return Results.Ok(new ResendNotificationResponse(request.CorrelationId())
            {
                NotificationId = notification.Id,
                ProviderMessageSid = notification.ProviderMessageSid,
                ProviderStatus = notification.ProviderStatus
            });
        }
        catch (Exception ex)
        {
            return ex.ToResult();
        }
    }
}
