using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int notificationId, ResendNotificationRequest request, IOrderNotificationService notificationService) =>
            {
                request ??= new ResendNotificationRequest();
                return await HandleAsync(notificationId, request, notificationService);
            })
            .Produces<ResendNotificationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(ResendNotificationRequest request, IOrderNotificationService notificationService)
        => HandleAsync(0, request, notificationService);

    private async Task<IResult> HandleAsync(
        int notificationId,
        ResendNotificationRequest request,
        IOrderNotificationService notificationService)
    {
        var notification = await notificationService.ResendAsync(notificationId, request.IdempotencyKey);
        return Results.Ok(new ResendNotificationResponse(request.CorrelationId())
        {
            NotificationId = notification.Id,
            Notification = NotificationDto.From(notification)
        });
    }
}
