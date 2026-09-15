using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IOperatorNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, IOperatorNotificationService operatorNotificationService) =>
            {
                request.NotificationId = notificationId;
                return await HandleAsync(request, operatorNotificationService);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, IOperatorNotificationService operatorNotificationService)
    {
        var response = new ResendNotificationResponse(request.CorrelationId());
        var notification = await operatorNotificationService.ResendAsync(request.NotificationId, request.IdempotencyKey);
        response.NotificationId = notification.Id;
        response.Notification = NotificationMapping.ToDto(notification);
        return Results.Ok(response);
    }
}
