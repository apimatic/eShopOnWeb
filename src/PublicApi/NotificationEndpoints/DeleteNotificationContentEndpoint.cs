using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: disposes of a message's text — at the provider, not only in
/// this application. The record that a message was sent, and its outcome, survives.
/// </summary>
public class DeleteNotificationContentEndpoint : IEndpoint<IResult, DeleteNotificationContentRequest, HttpContext>
{
    private readonly IRepository<OrderNotification> _notifications;
    private readonly ISmsProvider _smsProvider;

    public DeleteNotificationContentEndpoint(IRepository<OrderNotification> notifications, ISmsProvider smsProvider)
    {
        _notifications = notifications;
        _smsProvider = smsProvider;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, HttpContext httpContext) =>
            {
                return await HandleAsync(new DeleteNotificationContentRequest(notificationId), httpContext);
            })
            .Produces<DeleteNotificationContentResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteNotificationContentRequest request, HttpContext httpContext)
    {
        var notification = await _notifications.GetByIdAsync(request.NotificationId, httpContext.RequestAborted);
        if (notification is null)
        {
            return Results.NotFound();
        }

        if (notification.ContentRedacted)
        {
            return Results.Ok(new DeleteNotificationContentResponse(request.CorrelationId())
            {
                NotificationId = notification.Id,
                ContentRedacted = true
            });
        }

        if (notification.ProviderMessageSid is not null)
        {
            try
            {
                await _smsProvider.RedactMessageBodyAsync(notification.ProviderMessageSid, httpContext.RequestAborted);
            }
            catch (SmsProviderException ex)
            {
                // Local state is only changed once the provider has disposed of the body.
                return ProviderErrorResults.Map(ex);
            }
        }

        notification.RedactContent();
        await _notifications.UpdateAsync(notification, httpContext.RequestAborted);

        return Results.Ok(new DeleteNotificationContentResponse(request.CorrelationId())
        {
            NotificationId = notification.Id,
            ContentRedacted = true
        });
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

    public int NotificationId { get; set; }
    public bool ContentRedacted { get; set; }
}
