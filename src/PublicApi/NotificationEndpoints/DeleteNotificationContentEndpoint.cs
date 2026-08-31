using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: disposes of a message's content — at the provider as well as in this
/// application — while the fact it was sent, and its outcome, survive.
/// </summary>
public class DeleteNotificationContentEndpoint : IEndpoint<IResult, DeleteNotificationContentRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId:int}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IOrderNotificationService notificationService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(
                    new DeleteNotificationContentRequest(notificationId) { CancellationToken = cancellationToken },
                    notificationService);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteNotificationContentRequest request, IOrderNotificationService notificationService)
    {
        var result = await notificationService.RedactContentAsync(request.NotificationId, request.CancellationToken);
        return result.Status == RedactNotificationContentStatus.NotificationNotFound
            ? Results.NotFound()
            : Results.NoContent();
    }
}
