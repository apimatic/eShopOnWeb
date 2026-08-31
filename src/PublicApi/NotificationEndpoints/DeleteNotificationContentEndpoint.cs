using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Twilio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: disposes of the content of a message about a shopper. The body is
/// redacted at the provider (not merely hidden here); the record that a message was sent,
/// and its outcome, survives.
/// </summary>
public class DeleteNotificationContentEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IRepository<OrderNotification> notificationRepository,
                ISmsNotificationClient smsClient, IAppLogger<DeleteNotificationContentEndpoint> logger) =>
            {
                return await HandleAsync(new DeleteNotificationContentRequest(notificationId), notificationRepository, smsClient, logger);
            })
            .Produces<DeleteNotificationContentResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(
        DeleteNotificationContentRequest request,
        IRepository<OrderNotification> notificationRepository,
        ISmsNotificationClient smsClient,
        IAppLogger<DeleteNotificationContentEndpoint> logger)
    {
        var notification = await notificationRepository.GetByIdAsync(request.NotificationId);
        if (notification is null)
        {
            return Results.NotFound();
        }

        if (!notification.ContentRedacted && !string.IsNullOrEmpty(notification.MessageSid))
        {
            try
            {
                await smsClient.RedactMessageBodyAsync(notification.MessageSid);
            }
            catch (TwilioApiException ex)
            {
                logger.LogWarning("Provider redaction failed for notification {NotificationId}: status {HttpStatus}", notification.Id, ex.HttpStatusCode);
                return Results.StatusCode(StatusCodes.Status502BadGateway);
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
    public DeleteNotificationContentResponse(Guid correlationId) : base(correlationId) { }
    public DeleteNotificationContentResponse() { }

    public int NotificationId { get; set; }
    public bool ContentRedacted { get; set; }
}
