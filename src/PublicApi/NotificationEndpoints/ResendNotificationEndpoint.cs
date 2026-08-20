using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IOperatorOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, IOperatorOrderNotificationService operatorService) =>
            {
                return await HandleAsync(notificationId, request, operatorService);
            })
            .Produces<ResendNotificationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(ResendNotificationRequest request, IOperatorOrderNotificationService operatorService)
        => HandleAsync(0, request, operatorService);

    private async Task<IResult> HandleAsync(int notificationId, ResendNotificationRequest request, IOperatorOrderNotificationService operatorService)
    {
        if (notificationId <= 0)
        {
            return Results.BadRequest(new { message = "notificationId is required." });
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "IdempotencyKey is required." });
        }

        var notification = await operatorService.ResendAsync(notificationId, request.IdempotencyKey, default);
        var response = new ResendNotificationResponse(request.CorrelationId())
        {
            NotificationId = notification.Id,
            Notification = NotificationDto.From(notification)
        };

        return Results.Ok(response);
    }
}
