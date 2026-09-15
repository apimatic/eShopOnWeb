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
/// Operator action: disposes of a message's content so its text is no longer retrievable at the provider,
/// while the record that it was sent and what became of it survives. Administrator role required.
/// </summary>
public class DisposeNotificationContentEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, INotificationService service, CancellationToken ct) =>
            {
                return await HandleAsync(notificationId, service, ct);
            })
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, INotificationService service, CancellationToken ct)
    {
        var notification = await service.DisposeContentAsync(notificationId, ct);
        if (notification is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new
        {
            notificationId = notification.Id,
            contentRedacted = notification.ContentRedacted,
            status = notification.Status
        });
    }
}
