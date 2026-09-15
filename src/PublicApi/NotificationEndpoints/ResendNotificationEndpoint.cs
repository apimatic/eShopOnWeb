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

public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int notificationId, ResendNotificationRequest? request, HttpContext httpContext, IOrderNotificationService service) =>
            {
                var key = request?.IdempotencyKey;
                if (string.IsNullOrWhiteSpace(key))
                {
                    key = httpContext.Request.Headers["Idempotency-Key"].ToString();
                }

                return await HandleAsync(new ResendNotificationRequest
                {
                    NotificationId = notificationId,
                    IdempotencyKey = key ?? string.Empty
                }, service);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, IOrderNotificationService service)
    {
        var notification = await service.ResendAsync(request.NotificationId, request.IdempotencyKey);
        return Results.Ok(new ResendNotificationResponse
        {
            NotificationId = notification.Id,
            Status = notification.ProviderStatus,
            ProviderMessageSid = notification.ProviderMessageSid,
            Notification = NotificationDto.FromEntity(notification)
        });
    }
}
