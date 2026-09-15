using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// DELETE /api/notifications/{notificationId}/content — dispose of a message's content at the shopper's
/// request. Afterwards the text is no longer retrievable from the provider either, while the fact a message
/// was sent and what became of it survives. Administrator only.
/// </summary>
public class DisposeNotificationContentEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId:int}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int notificationId,
                IReadRepository<OrderNotification> repository,
                IOrderNotificationService notifications,
                CancellationToken ct) =>
            {
                var notification = await repository.GetByIdAsync(notificationId, ct);
                if (notification is null)
                {
                    return Results.NotFound();
                }

                // Redacts at the provider first, then locally. A provider-side failure surfaces (mapped by the
                // exception middleware) rather than us claiming the text is gone when it is not.
                await notifications.DisposeContentAsync(notificationId, ct);
                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }
}
