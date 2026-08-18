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
    public DisposeNotificationContentRequest(int notificationId) => NotificationId = notificationId;
}

/// <summary>
/// DELETE /api/notifications/{notificationId}/content — a shopper has asked for the content of a
/// message about them to be disposed of. Afterwards its text is no longer retrievable from the provider
/// either, while the fact a message was sent and what became of it survives. Operator-only.
/// </summary>
public class DisposeNotificationContentEndpoint : ApiEndpointBase,
    IEndpoint<IResult, DisposeNotificationContentRequest, INotificationService>
{
    public DisposeNotificationContentEndpoint(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor) { }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId:int}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, INotificationService notificationService) =>
                await HandleAsync(new DisposeNotificationContentRequest(notificationId), notificationService))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DisposeNotificationContentRequest request, INotificationService notificationService)
    {
        var found = await notificationService.DisposeContentAsync(request.NotificationId, Aborted);
        return found ? Results.NoContent() : Results.NotFound();
    }
}
