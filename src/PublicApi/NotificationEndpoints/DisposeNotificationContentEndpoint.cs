using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public record DisposeNotificationContentRequest(int NotificationId);

/// <summary>
/// DELETE /api/notifications/{notificationId}/content — operator action. Disposes of a message's content at
/// the provider and clears it here, while the fact that a message was sent and what became of it survives.
/// </summary>
public class DisposeNotificationContentEndpoint : IEndpoint<IResult, DisposeNotificationContentRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IOrderNotificationService service) =>
            {
                return await HandleAsync(new DisposeNotificationContentRequest(notificationId), service);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DisposeNotificationContentRequest request, IOrderNotificationService service)
    {
        var disposed = await service.DisposeContentAsync(request.NotificationId);
        return disposed ? Results.NoContent() : Results.NotFound(new { error = "Notification not found." });
    }
}
