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
/// Operator action (administrator only): disposes of the content of a message about a shopper. Afterwards
/// the text is no longer retrievable from the provider either — not merely hidden by this application —
/// while the fact a message was sent, and what became of it, survives. Returns 404 when the notification
/// does not exist.
/// </summary>
public class DisposeNotificationContentEndpoint : IEndpoint<IResult, INotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, INotificationService service) =>
            {
                var disposed = await service.DisposeContentAsync(notificationId);
                return disposed ? Results.NoContent() : Results.NotFound();
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(INotificationService service) =>
        Task.FromResult<IResult>(Results.Empty);
}
