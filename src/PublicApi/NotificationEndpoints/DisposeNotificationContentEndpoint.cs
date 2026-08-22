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
    public int NotificationId { get; init; }

    public DisposeNotificationContentRequest(int notificationId)
    {
        NotificationId = notificationId;
    }
}

public class DisposeNotificationContentEndpoint : IEndpoint<IResult, DisposeNotificationContentRequest, IOrderMessagingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IOrderMessagingService orderMessagingService) =>
            {
                return await HandleAsync(new DisposeNotificationContentRequest(notificationId), orderMessagingService);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DisposeNotificationContentRequest request, IOrderMessagingService orderMessagingService)
    {
        await orderMessagingService.DisposeContentAsync(request.NotificationId, default);
        return Results.NoContent();
    }
}
