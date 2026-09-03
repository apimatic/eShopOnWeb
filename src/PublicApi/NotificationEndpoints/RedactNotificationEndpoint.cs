using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class RedactNotificationRequest : BaseRequest
{
    public int NotificationId { get; init; }
    public RedactNotificationRequest(int notificationId) => NotificationId = notificationId;
}

public class RedactNotificationEndpoint : IEndpoint<IResult, RedactNotificationRequest, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int notificationId, IShopperOrderService orders, HttpContext http) =>
            {
                return await HandleAsync(new RedactNotificationRequest(notificationId), orders, http);
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(RedactNotificationRequest request, IShopperOrderService orders)
        => HandleAsync(request, orders, null!);

    private async Task<IResult> HandleAsync(RedactNotificationRequest request, IShopperOrderService orders, HttpContext http)
    {
        await orders.RedactContentAsync(request.NotificationId, http.RequestAborted);
        return Results.NoContent();
    }
}
