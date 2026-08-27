using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: disposes of a message's content. The text is redacted at the provider
/// itself (not merely hidden here), while the record that a message was sent, and what
/// became of it, survives.
/// </summary>
public class DeleteNotificationContentEndpoint : IEndpoint<IResult, int, HttpContext>
{
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IMessagingProvider _messagingProvider;

    public DeleteNotificationContentEndpoint(IRepository<OrderNotification> notificationRepository,
        IMessagingProvider messagingProvider)
    {
        _notificationRepository = notificationRepository;
        _messagingProvider = messagingProvider;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, HttpContext httpContext) =>
            {
                return await HandleAsync(notificationId, httpContext);
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, HttpContext httpContext)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, httpContext.RequestAborted);
        if (notification is null)
        {
            return Results.NotFound();
        }

        if (!notification.ContentRedacted)
        {
            if (notification.ProviderMessageSid is not null)
            {
                await _messagingProvider.RedactBodyAsync(notification.ProviderMessageSid, httpContext.RequestAborted);
            }

            notification.RedactContent();
            await _notificationRepository.UpdateAsync(notification, httpContext.RequestAborted);
        }

        return Results.NoContent();
    }
}
