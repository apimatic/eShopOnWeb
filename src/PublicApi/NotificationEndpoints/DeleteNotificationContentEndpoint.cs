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

public class DeleteNotificationContentResponse : BaseResponse
{
    public int NotificationId { get; set; }
    public bool ContentRedacted { get; set; }
}

/// <summary>
/// Operator action: disposes of the content of a message about a shopper. The text is
/// redacted at the provider too (not merely hidden here), while the record of the message
/// and its outcome survives.
/// </summary>
public class DeleteNotificationContentEndpoint : IEndpoint<IResult, int>
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
            (int notificationId) =>
            {
                return await HandleAsync(notificationId);
            })
            .Produces<DeleteNotificationContentResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId);
        if (notification is null)
        {
            return Results.NotFound();
        }

        if (!notification.ContentRedacted)
        {
            if (notification.ProviderMessageSid is not null)
            {
                var redacted = await _smsProvider.RedactMessageBodyAsync(notification.ProviderMessageSid);
                if (!redacted)
                {
                    // Do not redact locally while the provider still holds the text.
                    return Results.Json(new { error = "The provider could not redact the message content; nothing was changed locally." }, statusCode: 502);
                }
            }
            notification.MarkContentRedacted();
            await _notificationRepository.UpdateAsync(notification);
        }

        return Results.Ok(new DeleteNotificationContentResponse
        {
            NotificationId = notification.Id,
            ContentRedacted = notification.ContentRedacted
        });
    }
}
