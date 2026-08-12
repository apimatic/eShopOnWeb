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
/// Operator action, on a shopper's request: disposes of a message's content so its text is no
/// longer retrievable from the provider either — not merely hidden by this application — while
/// the fact that a message was sent, and what became of it, survives.
/// </summary>
public class DeleteNotificationContentEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int notificationId,
                IOrderNotificationService notifications) =>
            {
                var disposed = await notifications.DisposeContentAsync(notificationId);
                return disposed ? Results.NoContent() : Results.NotFound();
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }
}
