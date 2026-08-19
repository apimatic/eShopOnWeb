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

public class DisposeNotificationContentRequest : BaseRequest
{
    public int NotificationId { get; set; }
}

/// <summary>
/// DELETE /api/notifications/{notificationId}/content — dispose of a message's text at the
/// shopper's request. The text is redacted at the provider and cleared locally; the fact a
/// message was sent, and what became of it, survives.
/// </summary>
public class DisposeNotificationContentEndpoint : IEndpoint<IResult, DisposeNotificationContentRequest, INotificationAdminService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, INotificationAdminService service) =>
            {
                return await HandleAsync(new DisposeNotificationContentRequest { NotificationId = notificationId }, service);
            })
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DisposeNotificationContentRequest request, INotificationAdminService service)
    {
        var found = await service.DisposeContentAsync(request.NotificationId, CancellationToken.None);
        return found ? Results.NoContent() : Results.NotFound();
    }
}
