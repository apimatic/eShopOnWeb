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
/// Operator action: disposes of a message's content at the provider so its text is no longer
/// retrievable, while the fact it was sent and its outcome survive. Restricted to the administrator role.
/// </summary>
public class DeleteNotificationContentEndpoint : IEndpoint<IResult, int, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IOrderNotificationService service) =>
            {
                return await HandleAsync(notificationId, service);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, IOrderNotificationService service)
    {
        var result = await service.RedactContentAsync(notificationId);
        return result.IsSuccess ? Results.NoContent() : result.ToFailureResult();
    }
}
