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
/// Operator action (on a shopper's behalf): disposes of the content of a message so its text is no longer
/// retrievable from the provider either, while the fact it was sent and what became of it survives.
/// </summary>
public class DisposeNotificationContentEndpoint : IEndpoint<IResult, DisposeNotificationContentRequest, INotificationOperationsService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId:int}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, INotificationOperationsService service) =>
            {
                return await HandleAsync(new DisposeNotificationContentRequest { NotificationId = notificationId }, service);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DisposeNotificationContentRequest request, INotificationOperationsService service)
    {
        // A provider failure surfaces as SmsGatewayException -> 502 (disposal incomplete), handled centrally.
        var disposed = await service.DisposeContentAsync(request.NotificationId);
        return disposed ? Results.NoContent() : Results.NotFound();
    }
}
