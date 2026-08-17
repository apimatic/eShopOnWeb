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
/// Operator action on behalf of a shopper: disposes of a message's content. The text is redacted at
/// the provider (no longer retrievable there) and the stored copy is cleared, while the fact a
/// message was sent and what became of it survives.
/// </summary>
public class DisposeNotificationContentEndpoint : IEndpoint<IResult, int, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IOrderNotificationService service) =>
            {
                return await HandleAsync(notificationId, service);
            })
            .Produces<DisposeNotificationContentResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, IOrderNotificationService service)
    {
        var outcome = await service.DisposeContentAsync(notificationId);
        if (outcome.NotFound || outcome.Notification is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new DisposeNotificationContentResponse
        {
            NotificationId = outcome.Notification.Id,
            ContentDisposed = outcome.Notification.ContentDisposed
        });
    }
}
