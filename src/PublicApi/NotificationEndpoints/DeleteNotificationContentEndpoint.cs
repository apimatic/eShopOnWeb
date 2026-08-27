using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Disposes of a message's content (operator): the body is redacted at the
/// provider so it is no longer retrievable there, and removed locally. The
/// record that a message was sent, and its outcome, survives.
/// </summary>
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class DeleteNotificationContentEndpoint : EndpointBaseAsync
    .WithRequest<int>
    .WithoutResult
{
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IMessagingClient _messagingClient;

    public DeleteNotificationContentEndpoint(IRepository<OrderNotification> notifications, IMessagingClient messagingClient)
    {
        _notifications = notifications;
        _messagingClient = messagingClient;
    }

    [HttpDelete("api/notifications/{notificationId}/content")]
    [SwaggerOperation(Summary = "Disposes of a message's content at the provider and locally (operator)", Tags = new[] { "NotificationEndpoints" })]
    public override async Task<ActionResult> HandleAsync(
        [FromRoute(Name = "notificationId")] int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null) return NotFound();

        if (notification.ContentRedacted)
        {
            return NoContent();
        }

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            await _messagingClient.RedactMessageBodyAsync(notification.ProviderMessageSid, cancellationToken);
        }

        notification.RedactContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
        return NoContent();
    }
}
