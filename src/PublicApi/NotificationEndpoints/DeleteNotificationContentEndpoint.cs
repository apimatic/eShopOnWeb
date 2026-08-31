using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class DeleteNotificationContentResponse : BaseResponse
{
    public int NotificationId { get; set; }
    public bool ContentRedacted { get; set; }
}

/// <summary>
/// Disposes of a message's content (operator, on a shopper's request). The text is redacted at
/// the provider itself — not merely hidden here — while the record that a message was sent,
/// and its outcome, survive.
/// </summary>
public class DeleteNotificationContentEndpoint : IEndpoint<IResult, int, CancellationToken>
{
    private readonly IRepository<OrderNotification> _notifications;
    private readonly TwilioMessagingService _messaging;

    public DeleteNotificationContentEndpoint(IRepository<OrderNotification> notifications,
        TwilioMessagingService messaging)
    {
        _notifications = notifications;
        _messaging = messaging;
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
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, CancellationToken ct)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, ct);
        if (notification is null)
        {
            return Results.NotFound();
        }

        if (notification.ContentRedacted)
        {
            return Results.Ok(new DeleteNotificationContentResponse
            {
                NotificationId = notificationId,
                ContentRedacted = true
            });
        }

        if (notification.ProviderMessageSid is not null)
        {
            try
            {
                var outcome = await _messaging.RedactMessageBodyAsync(notification.ProviderMessageSid, ct);
                notification.UpdateProviderOutcome(outcome.Status, outcome.ErrorCode, outcome.ErrorMessage);
            }
            catch (MessagingException)
            {
                return Results.Problem(
                    "The message content could not be disposed of at the provider; the local record was left untouched.",
                    statusCode: 502);
            }
        }

        notification.RedactContent();
        await _notifications.UpdateAsync(notification, ct);

        return Results.Ok(new DeleteNotificationContentResponse
        {
            NotificationId = notificationId,
            ContentRedacted = true
        });
    }
}
