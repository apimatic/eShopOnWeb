using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SmsNotifications.NotificationEndpoints;

public class DisposeNotificationContentRequest : BaseRequest
{
    public int NotificationId { get; set; }
    public DisposeNotificationContentRequest(int notificationId) => NotificationId = notificationId;
}

/// <summary>
/// DELETE /api/notifications/{notificationId}/content &ndash; dispose of a message's text at the
/// provider (not merely hide it here) on the shopper's request. The record and outcome survive.
/// </summary>
public class DisposeNotificationContentEndpoint : IEndpoint<IResult, DisposeNotificationContentRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IOrderNotificationService notificationService) =>
            {
                return await HandleAsync(new DisposeNotificationContentRequest(notificationId), notificationService);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DisposeNotificationContentRequest request, IOrderNotificationService notificationService)
    {
        var disposed = await notificationService.DisposeContentAsync(request.NotificationId);
        return disposed ? Results.NoContent() : Results.NotFound();
    }
}
