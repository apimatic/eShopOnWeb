using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action on a shopper's behalf: dispose of a message's content. The text is redacted at the
/// provider (no longer retrievable there), while the fact a message was sent and what became of it survive.
/// </summary>
public class DisposeNotificationContentEndpoint : IEndpoint<IResult, int, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, HttpContext httpContext) =>
            {
                return await HandleAsync(notificationId, httpContext);
            })
            .Produces<DisposeNotificationContentResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, HttpContext httpContext)
    {
        var cancellationToken = httpContext.RequestAborted;
        var notificationRepository = httpContext.RequestServices.GetRequiredService<IRepository<OrderNotification>>();
        var notificationService = httpContext.RequestServices.GetRequiredService<IOrderNotificationService>();

        var notification = await notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
            return Results.NotFound();

        var disposed = await notificationService.DisposeContentAsync(notification, cancellationToken);

        return Results.Ok(new DisposeNotificationContentResponse
        {
            NotificationId = notification.Id,
            ContentRedacted = notification.ContentRedacted,
            Message = disposed
                ? "Message content was disposed of at the provider."
                : "No provider message content to dispose of; the record and its outcome are unchanged."
        });
    }
}

public class DisposeNotificationContentResponse : BaseResponse
{
    public int NotificationId { get; set; }
    public bool ContentRedacted { get; set; }
    public string Message { get; set; } = string.Empty;
}
