using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class RedactNotificationContentEndpoint : IEndpoint<IResult, RedactNotificationContentRequest, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IShopperOrderService service, HttpContext http) =>
            {
                return await HandleAsync(new RedactNotificationContentRequest(notificationId), service, http.RequestAborted);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(RedactNotificationContentRequest request, IShopperOrderService service) =>
        HandleAsync(request, service, default);

    private async Task<IResult> HandleAsync(
        RedactNotificationContentRequest request,
        IShopperOrderService service,
        System.Threading.CancellationToken cancellationToken)
    {
        await service.RedactContentAsync(request.NotificationId, cancellationToken);
        return Results.NoContent();
    }
}

public class RedactNotificationContentRequest : BaseRequest
{
    public int NotificationId { get; init; }

    public RedactNotificationContentRequest(int notificationId)
    {
        NotificationId = notificationId;
    }
}
