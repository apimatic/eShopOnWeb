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
/// Operator action: disposes of a message's content. Afterwards its text is no longer retrievable
/// from the provider either, while the fact that a message was sent and what became of it survives.
/// </summary>
public class RedactNotificationContentEndpoint : IEndpoint<IResult, RedactContentRequest, INotificationManagementService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, INotificationManagementService service) =>
                await HandleAsync(new RedactContentRequest(notificationId), service))
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(RedactContentRequest request, INotificationManagementService service)
    {
        var disposed = await service.RedactContentAsync(request.NotificationId);
        return disposed ? Results.NoContent() : Results.NotFound();
    }
}
