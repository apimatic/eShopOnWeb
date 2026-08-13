using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class DisposeNotificationContentResponse
{
    public int NotificationId { get; set; }
    public bool ContentDisposed { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Operator action: disposes of a message's content on a shopper's request. The text is redacted at
/// the provider and cleared locally, so it is no longer retrievable either place; the fact a message
/// was sent, and what became of it, survives. Restricted to the administrator role.
/// </summary>
public class DisposeNotificationContentEndpoint : IEndpoint<IResult, int, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, HttpContext http) => await HandleAsync(notificationId, http))
            .Produces<DisposeNotificationContentResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, HttpContext http)
    {
        var service = http.RequestServices.GetRequiredService<IOrderNotificationService>();
        var notification = await service.DisposeContentAsync(notificationId, http.RequestAborted);
        if (notification is null)
            return Results.NotFound();

        return Results.Ok(new DisposeNotificationContentResponse
        {
            NotificationId = notification.Id,
            ContentDisposed = notification.ContentDisposed,
            Status = notification.Status
        });
    }
}
