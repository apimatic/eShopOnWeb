using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SmsNotifications;

/// <summary>
/// DELETE /api/notifications/{notificationId}/content — disposes of a message's content. Afterwards the text
/// is no longer retrievable from the provider either, while the fact a message was sent and its outcome
/// survive. A provider failure surfaces as an error (the content was NOT disposed of).
/// </summary>
public class DisposeNotificationContentEndpoint
    : IEndpoint<IResult, int, IOrderNotificationService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId:int}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IOrderNotificationService service, HttpContext http) =>
                await HandleAsync(notificationId, service, http))
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, IOrderNotificationService service, HttpContext http)
    {
        var disposed = await service.DisposeContentAsync(notificationId, http.RequestAborted);
        return disposed is null ? Results.NotFound() : Results.NoContent();
    }
}
