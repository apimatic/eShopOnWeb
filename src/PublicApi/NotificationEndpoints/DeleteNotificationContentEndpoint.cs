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
/// Disposes of a message's content (operator): the text is redacted at the provider
/// and removed locally, while the fact the message was sent and its outcome survive.
/// </summary>
public class DeleteNotificationContentEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IOrderNotificationService notificationService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(new DeleteNotificationContentRequest { NotificationId = notificationId }, notificationService, cancellationToken);
            })
            .Produces<DeleteNotificationContentResponse>()
            .WithTags("NotificationEndpoints");
    }

    private async Task<IResult> HandleAsync(DeleteNotificationContentRequest request, IOrderNotificationService notificationService, CancellationToken cancellationToken)
    {
        await notificationService.DeleteContentAsync(request.NotificationId, cancellationToken);
        return Results.Ok(new DeleteNotificationContentResponse(request.CorrelationId()) { NotificationId = request.NotificationId });
    }
}
