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
            (int notificationId, IShopperOrderService orderService, HttpContext httpContext) =>
            {
                return await HandleAsync(new RedactNotificationContentRequest(notificationId), httpContext, orderService);
            })
            .Produces<RedactNotificationContentResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(RedactNotificationContentRequest request, IShopperOrderService orderService)
        => HandleAsync(request, null!, orderService);

    private async Task<IResult> HandleAsync(
        RedactNotificationContentRequest request,
        HttpContext httpContext,
        IShopperOrderService orderService)
    {
        var response = new RedactNotificationContentResponse(request.CorrelationId())
        {
            NotificationId = request.NotificationId
        };
        await orderService.RedactContentAsync(request.NotificationId, httpContext.RequestAborted);
        return Results.Ok(response);
    }
}
