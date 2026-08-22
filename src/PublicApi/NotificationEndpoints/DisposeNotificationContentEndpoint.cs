using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class DisposeNotificationContentRequest : BaseRequest
{
    public int NotificationId { get; init; }

    public DisposeNotificationContentRequest(int notificationId) => NotificationId = notificationId;
}

public class DisposeNotificationContentEndpoint : IEndpoint<IResult, DisposeNotificationContentRequest, INotificationOperatorService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public DisposeNotificationContentEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, INotificationOperatorService service) =>
            {
                return await HandleAsync(new DisposeNotificationContentRequest(notificationId), service);
            })
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DisposeNotificationContentRequest request, INotificationOperatorService service)
    {
        var http = _httpContextAccessor.HttpContext!;
        await service.DisposeContentAsync(request.NotificationId, http.RequestAborted);
        return Results.NoContent();
    }
}
