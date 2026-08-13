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
/// Operator action: disposes of a message's content at the request of the shopper it is about. The
/// text is removed at the provider too — not merely hidden here — while the fact that a message was
/// sent, and what became of it, survives.
/// DELETE /api/notifications/{notificationId}/content
/// </summary>
public class DisposeNotificationContentEndpoint : IEndpoint<IResult, int, INotificationAdminService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, INotificationAdminService service) => await HandleAsync(notificationId, service))
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, INotificationAdminService service)
    {
        var disposed = await service.DisposeContentAsync(notificationId);
        return disposed ? Results.NoContent() : Results.NotFound();
    }
}
