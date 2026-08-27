using System;
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
/// Disposes of a message's content (operator action, on a shopper's request).
/// The text is redacted at the provider — not merely hidden here — while the
/// record that a message was sent, and its outcome, survives.
/// </summary>
public class DeleteNotificationContentEndpoint : IEndpoint<IResult, DeleteNotificationContentRequest>
{
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly ISmsService _smsService;

    public DeleteNotificationContentEndpoint(IRepository<OrderNotification> notificationRepository,
        ISmsService smsService)
    {
        _notificationRepository = notificationRepository;
        _smsService = smsService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId) =>
            {
                return await HandleAsync(new DeleteNotificationContentRequest(notificationId));
            })
            .Produces<DeleteNotificationContentResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteNotificationContentRequest request)
    {
        var notification = await _notificationRepository.GetByIdAsync(request.NotificationId);
        if (notification is null)
        {
            return Results.NotFound();
        }

        if (!notification.IsContentRedacted)
        {
            if (notification.MessageSid is not null)
            {
                // Redact the body at the provider so it is no longer retrievable there.
                await _smsService.RedactBodyAsync(notification.MessageSid);
            }

            notification.MarkContentRedacted();
            await _notificationRepository.UpdateAsync(notification);
        }

        var response = new DeleteNotificationContentResponse(request.CorrelationId())
        {
            NotificationId = notification.Id,
            IsContentRedacted = notification.IsContentRedacted,
            Status = notification.Status
        };
        return Results.Ok(response);
    }
}

public class DeleteNotificationContentRequest : BaseRequest
{
    public DeleteNotificationContentRequest(int notificationId)
    {
        NotificationId = notificationId;
    }

    public int NotificationId { get; }
}

public class DeleteNotificationContentResponse : BaseResponse
{
    public DeleteNotificationContentResponse(Guid correlationId) : base(correlationId) { }
    public DeleteNotificationContentResponse() { }

    public int NotificationId { get; set; }
    public bool IsContentRedacted { get; set; }
    public string Status { get; set; } = string.Empty;
}
