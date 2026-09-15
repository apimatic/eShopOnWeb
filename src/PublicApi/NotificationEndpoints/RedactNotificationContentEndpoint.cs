using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class RedactNotificationContentRequest : BaseRequest
{
    public int NotificationId { get; init; }

    public RedactNotificationContentRequest(int notificationId)
    {
        NotificationId = notificationId;
    }
}

public class RedactNotificationContentResponse : BaseResponse
{
    public string Status { get; set; } = "Redacted";
}

public class RedactNotificationContentEndpoint : IEndpoint<IResult, RedactNotificationContentRequest, IOperatorOrderNotificationService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RedactNotificationContentEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IOperatorOrderNotificationService service) =>
            {
                return await HandleAsync(new RedactNotificationContentRequest(notificationId), service);
            })
            .Produces<RedactNotificationContentResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(RedactNotificationContentRequest request, IOperatorOrderNotificationService service)
    {
        var ct = _httpContextAccessor.HttpContext?.RequestAborted ?? default;
        await service.RedactContentAsync(request.NotificationId, ct);
        return Results.Ok(new RedactNotificationContentResponse());
    }
}
