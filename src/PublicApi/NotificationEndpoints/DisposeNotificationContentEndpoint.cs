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
/// Operator action on a shopper's behalf: disposes of a message's content at the provider and
/// locally. Afterwards the text is no longer retrievable anywhere, while the fact a message was
/// sent and what became of it survives.
/// </summary>
public class DisposeNotificationContentEndpoint : IEndpoint<IResult, DisposeNotificationContentRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IOrderNotificationService notificationService) =>
                await HandleAsync(new DisposeNotificationContentRequest(notificationId), notificationService))
            .Produces<DisposeNotificationContentResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DisposeNotificationContentRequest request, IOrderNotificationService notificationService)
    {
        var disposed = await notificationService.DisposeContentAsync(request.NotificationId);
        if (!disposed)
        {
            return Results.NotFound();
        }

        return Results.Ok(new DisposeNotificationContentResponse(request.CorrelationId()));
    }
}
