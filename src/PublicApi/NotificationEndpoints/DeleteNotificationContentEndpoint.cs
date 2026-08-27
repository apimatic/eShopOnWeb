using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class DeleteNotificationContentResponse : BaseResponse
{
    public int NotificationId { get; set; }
    public bool BodyRedacted { get; set; }
}

/// <summary>
/// Disposes of a message's content (operator). The text is redacted at the provider so it
/// is no longer retrievable there either; the record that a message was sent, and its
/// outcome, survives.
/// </summary>
public class DeleteNotificationContentEndpoint : IEndpoint<IResult, int>
{
    private readonly IRepository<Notification> _notificationRepository;
    private readonly ISmsService _smsService;

    public DeleteNotificationContentEndpoint(IRepository<Notification> notificationRepository, ISmsService smsService)
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

        if (!notification.BodyRedacted)
        {
            try
            {
                await _smsService.RedactMessageBodyAsync(notification.MessageSid);
            }
            catch (SmsProviderException ex)
            {
                return Results.Json(new { message = $"The provider could not redact the message content (error code {ex.ProviderErrorCode})." }, statusCode: 502);
            }

            notification.RedactBody();
            await _notificationRepository.UpdateAsync(notification);
        }

        return Results.Ok(new DeleteNotificationContentResponse
        {
            NotificationId = notification.Id,
            BodyRedacted = notification.BodyRedacted
        });
    }
}
