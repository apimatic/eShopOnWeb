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
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class DeleteNotificationContentResponse : BaseResponse
{
    public DeleteNotificationContentResponse(Guid correlationId) : base(correlationId) { }
    public DeleteNotificationContentResponse() { }

    public int NotificationId { get; set; }
    public bool ContentDisposed { get; set; }
}

/// <summary>
/// Operator action: disposes of a message's content at the shopper's request. The text is
/// redacted at the provider too, so it is no longer retrievable there; the record that a
/// message was sent, and its outcome, survives.
/// </summary>
public class DeleteNotificationContentEndpoint : IEndpoint<IResult, IRepository<OrderNotification>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IRepository<OrderNotification> notificationRepository, ISmsGateway smsGateway, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(notificationId, notificationRepository, smsGateway, cancellationToken);
            })
            .Produces<DeleteNotificationContentResponse>()
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(IRepository<OrderNotification> notificationRepository)
        => throw new NotSupportedException("Use the routed overload with the notification id.");

    private async Task<IResult> HandleAsync(int notificationId, IRepository<OrderNotification> notificationRepository,
        ISmsGateway smsGateway, CancellationToken cancellationToken)
    {
        var notification = await notificationRepository.FirstOrDefaultAsync(
            new NotificationByIdSpecification(notificationId), cancellationToken);
        if (notification is null)
        {
            return Results.NotFound();
        }

        if (!notification.ContentDisposed && notification.ProviderMessageSid is not null)
        {
            await smsGateway.RedactMessageBodyAsync(notification.ProviderMessageSid, cancellationToken);
        }

        notification.MarkContentDisposed();
        await notificationRepository.UpdateAsync(notification, cancellationToken);

        var response = new DeleteNotificationContentResponse
        {
            NotificationId = notification.Id,
            ContentDisposed = notification.ContentDisposed
        };
        return Results.Ok(response);
    }
}
