using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// A shopper has asked for the content of a message about them to be disposed of. The text is redacted
/// at the provider (so it is no longer retrievable there) and dropped here, while the fact a message
/// was sent and what became of it survives. Administrator role only.
/// </summary>
public class DisposeNotificationContentEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId:int}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ISmsNotificationService service) =>
            {
                // A missing notification throws NotificationNotFoundException, mapped to 404. A provider
                // failure to redact surfaces as an error, so success is only reported once the text is gone.
                await service.DisposeContentAsync(notificationId);
                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }
}
