using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Middleware;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Disposes of a message's content (operator action, on a shopper's request). The text
/// is redacted at the provider itself — not merely hidden here — while the record that
/// a message was sent, and what became of it, survives.
/// </summary>
public class DeleteNotificationContentEndpoint : IEndpoint
{
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IMessagingService _messagingService;

    public DeleteNotificationContentEndpoint(
        IRepository<OrderNotification> notificationRepository,
        IMessagingService messagingService)
    {
        _notificationRepository = notificationRepository;
        _messagingService = messagingService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, CancellationToken ct) =>
            {
                return await HandleAsync(notificationId, ct);
            })
            .Produces<DeleteNotificationContentResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, CancellationToken ct)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, ct);
        if (notification is null)
        {
            return Results.NotFound();
        }

        if (!notification.ContentRedacted && notification.MessageSid != null)
        {
            try
            {
                await _messagingService.RedactMessageBodyAsync(notification.MessageSid, ct);
            }
            catch (MessagingException ex)
            {
                return ProviderErrorResults.Map(ex);
            }
        }

        notification.RedactContent();
        await _notificationRepository.UpdateAsync(notification, ct);

        var response = new DeleteNotificationContentResponse(Guid.NewGuid())
        {
            NotificationId = notification.Id,
            ContentRedacted = true
        };
        return Results.Ok(response);
    }
}
