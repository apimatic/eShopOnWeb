using System.Threading.Tasks;
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

public class RedactNotificationResponse : BaseResponse
{
    public int NotificationId { get; set; }
    public bool ContentRedacted { get; set; } = true;
}

public class RedactNotificationContentEndpoint : IEndpoint<IResult, RedactNotificationRequest, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, HttpContext httpContext, IShopperOrderService service) =>
            {
                return await HandleAsync(new RedactNotificationRequest(notificationId), httpContext, service);
            })
            .Produces<RedactNotificationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(RedactNotificationRequest request, IShopperOrderService service)
        => HandleAsync(request, null!, service);

    private async Task<IResult> HandleAsync(RedactNotificationRequest request, HttpContext httpContext, IShopperOrderService service)
    {
        await service.RedactContentAsync(request.NotificationId, httpContext.RequestAborted);
        return Results.Ok(new RedactNotificationResponse { NotificationId = request.NotificationId, ContentRedacted = true });
    }
}
