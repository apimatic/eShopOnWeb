using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class DisposeNotificationContentEndpoint : IEndpoint<IResult, int, IOperatorOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IOperatorOrderNotificationService operatorService) =>
            {
                return await HandleAsync(notificationId, operatorService);
            })
            .Produces<NotificationDto>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, IOperatorOrderNotificationService operatorService)
    {
        var notification = await operatorService.DisposeContentAsync(notificationId, default);
        if (notification is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(NotificationDto.From(notification));
    }
}
