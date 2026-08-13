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
/// Operator action: disposes of a message's content at the shopper's request. Afterwards the text is no
/// longer retrievable from the provider either, while the fact that it was sent and what became of it
/// survive. Restricted to administrators.
/// </summary>
public class DisposeNotificationContentEndpoint : IEndpoint<IResult, DisposeNotificationContentRequest, ISmsNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ISmsNotificationService service) =>
                await HandleAsync(new DisposeNotificationContentRequest { NotificationId = notificationId }, service))
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DisposeNotificationContentRequest request, ISmsNotificationService service)
    {
        var result = await service.DisposeContentAsync(request.NotificationId);
        return result == DisposeResultCode.Disposed
            ? Results.Ok(new { notificationId = request.NotificationId, disposed = true })
            : Results.NotFound(new { message = "Notification not found." });
    }
}
