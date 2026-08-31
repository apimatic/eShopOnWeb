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
/// Operator action: disposes of a message's content after a shopper's request. The text is
/// redacted at the provider itself (verified by re-reading it) and dropped from our own
/// record; the fact a message was sent, and its outcome, survive.
/// </summary>
public class DeleteNotificationContentEndpoint : IEndpoint<IResult, int, IRepository<OrderNotification>>
{
    private readonly ISmsProvider _smsProvider;

    public DeleteNotificationContentEndpoint(ISmsProvider smsProvider)
    {
        _smsProvider = smsProvider;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IRepository<OrderNotification> notificationRepository) =>
            {
                return await HandleAsync(notificationId, notificationRepository);
            })
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, IRepository<OrderNotification> notificationRepository)
    {
        var notification = await notificationRepository.GetByIdAsync(notificationId);
        if (notification is null)
        {
            return Results.NotFound();
        }

        if (notification.ContentRedacted)
        {
            return Results.Ok(new DeleteNotificationContentResponse
            {
                NotificationId = notification.Id,
                ContentRedacted = true
            });
        }

        if (notification.MessageSid is not null)
        {
            var result = await _smsProvider.RedactMessageBodyAsync(notification.MessageSid);
            if (!result.Success)
            {
                return Results.Problem(
                    detail: result.ErrorMessage ?? "The provider could not confirm the content was disposed of.",
                    statusCode: StatusCodes.Status502BadGateway);
            }
        }

        notification.MarkContentRedacted();
        await notificationRepository.UpdateAsync(notification);

        return Results.Ok(new DeleteNotificationContentResponse
        {
            NotificationId = notification.Id,
            ContentRedacted = true
        });
    }
}
