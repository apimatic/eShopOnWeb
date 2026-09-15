using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// DELETE /api/notifications/{notificationId}/content — dispose of a message's content at the shopper's
/// request. Afterwards the text is no longer retrievable from the provider either, while the record of
/// the message and its outcome survives. Administrator only.
/// </summary>
public class DisposeNotificationContentEndpoint : IEndpoint<IResult, int, INotificationOperationsService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, INotificationOperationsService service) =>
            {
                return await HandleAsync(notificationId, service);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, INotificationOperationsService service)
    {
        var disposed = await service.DisposeContentAsync(notificationId);
        return disposed ? Results.NoContent() : Results.NotFound();
    }
}
