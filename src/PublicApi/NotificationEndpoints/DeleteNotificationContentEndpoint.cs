using System.Collections.Generic;
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

public class DeleteNotificationContentEndpoint : IEndpoint<IResult, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int notificationId, IOrderNotificationService notificationService) =>
            {
                try
                {
                    var notification = await notificationService.RedactContentAsync(notificationId);
                    return Results.Ok(NotificationDto.From(notification));
                }
                catch (KeyNotFoundException)
                {
                    return Results.NotFound();
                }
            })
            .Produces<NotificationDto>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderNotificationService notificationService)
    {
        return Task.FromResult<IResult>(Results.StatusCode(StatusCodes.Status501NotImplemented));
    }
}
