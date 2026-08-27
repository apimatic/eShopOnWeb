using System;
using System.Threading;
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
/// Operator action: disposes of the text of a message about a shopper. The body
/// is redacted at the provider itself (not merely hidden here), while the record
/// that a message was sent, and its outcome, survives.
/// </summary>
public class DeleteNotificationContentEndpoint : IEndpoint<IResult, DeleteNotificationContentRequest>
{
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly ISmsProvider _smsProvider;

    public DeleteNotificationContentEndpoint(IRepository<OrderNotification> notificationRepository, ISmsProvider smsProvider)
    {
        _notificationRepository = notificationRepository;
        _smsProvider = smsProvider;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(new DeleteNotificationContentRequest(notificationId), cancellationToken);
            })
            .Produces<DeleteNotificationContentResponse>()
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(DeleteNotificationContentRequest request)
        => HandleAsync(request, default);

    private async Task<IResult> HandleAsync(DeleteNotificationContentRequest request, CancellationToken cancellationToken)
    {
        var notification = await _notificationRepository.GetByIdAsync(request.NotificationId, cancellationToken);
        if (notification is null)
        {
            return Results.NotFound();
        }

        if (!notification.ContentDisposed)
        {
            if (notification.ProviderMessageSid is not null)
            {
                var redacted = await _smsProvider.RedactMessageBodyAsync(notification.ProviderMessageSid, cancellationToken);
                if (!redacted)
                {
                    return Results.Problem("The provider could not redact the message content; nothing was disposed of locally.",
                        statusCode: StatusCodes.Status502BadGateway);
                }
            }

            notification.MarkContentDisposed();
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
        }

        return Results.Ok(new DeleteNotificationContentResponse(request.CorrelationId())
        {
            NotificationId = notification.Id,
            ContentDisposed = true,
            Status = notification.Status
        });
    }
}

public class DeleteNotificationContentRequest : BaseRequest
{
    public DeleteNotificationContentRequest(int notificationId)
    {
        NotificationId = notificationId;
    }

    public int NotificationId { get; set; }
}

public class DeleteNotificationContentResponse : BaseResponse
{
    public DeleteNotificationContentResponse(Guid correlationId) : base(correlationId) {}
    public DeleteNotificationContentResponse() {}

    public int NotificationId { get; set; }
    public bool ContentDisposed { get; set; }
    public string Status { get; set; } = string.Empty;
}
