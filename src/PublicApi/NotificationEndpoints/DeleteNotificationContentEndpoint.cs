using System;
using System.Net;
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
/// Operator action: disposes of a message's content after a shopper's erasure request.
/// The body is redacted at the provider — not merely hidden here — while the record that
/// a message was sent, and its outcome, survives.
/// </summary>
public class DeleteNotificationContentEndpoint : IEndpoint<IResult, DeleteNotificationContentRequest, IRepository<OrderNotification>>
{
    private readonly INotificationGateway _gateway;

    public DeleteNotificationContentEndpoint(INotificationGateway gateway)
    {
        _gateway = gateway;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IRepository<OrderNotification> notificationRepository) =>
            {
                return await HandleAsync(new DeleteNotificationContentRequest(notificationId), notificationRepository);
            })
            .Produces<DeleteNotificationContentResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteNotificationContentRequest request, IRepository<OrderNotification> notificationRepository)
    {
        var notification = await notificationRepository.GetByIdAsync(request.NotificationId);
        if (notification is null)
        {
            return Results.NotFound();
        }

        if (!notification.ContentRedacted && notification.MessageSid is not null)
        {
            try
            {
                await _gateway.RedactMessageBodyAsync(notification.MessageSid);
            }
            catch (NotificationProviderException ex) when (ex.ProviderStatusCode == HttpStatusCode.NotFound)
            {
                // The provider no longer holds the message — the erasure goal is met.
            }
            catch (NotificationProviderException ex)
            {
                return Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
            }
        }

        notification.RedactContent();
        await notificationRepository.UpdateAsync(notification);

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
    public DeleteNotificationContentResponse(Guid correlationId) : base(correlationId) {}
    public DeleteNotificationContentResponse() {}

    public int NotificationId { get; set; }
    public bool ContentRedacted { get; set; }
}
