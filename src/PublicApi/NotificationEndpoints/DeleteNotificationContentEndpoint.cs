using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: disposes of the content of a message. The text is redacted at the
/// provider (not merely hidden here); the fact a message was sent and its outcome survive.
/// </summary>
public class DeleteNotificationContentEndpoint : IEndpoint<IResult, int, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, HttpContext httpContext) =>
            {
                return await HandleAsync(notificationId, httpContext);
            })
            .Produces<DeleteNotificationContentResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, HttpContext httpContext)
    {
        var notificationRepository = httpContext.RequestServices.GetRequiredService<IRepository<OrderNotification>>();
        var smsProvider = httpContext.RequestServices.GetRequiredService<ISmsProvider>();

        var notification = await notificationRepository.GetByIdAsync(notificationId);
        if (notification is null)
        {
            return Results.NotFound();
        }

        if (!notification.ContentDisposed
            && notification.AcceptedByProvider
            && notification.ProviderMessageSid is not null)
        {
            try
            {
                // Redact the body at the provider so the text is no longer retrievable there.
                await smsProvider.RedactMessageBodyAsync(notification.ProviderMessageSid, httpContext.RequestAborted);
            }
            catch (SmsProviderException ex)
            {
                // Do not mark disposed locally: the text is still retrievable at the
                // provider, so the operator must be able to retry.
                return Results.Problem($"The provider could not redact the message content: {ex.Message}", statusCode: 502);
            }
        }

        notification.MarkContentDisposed();
        await notificationRepository.UpdateAsync(notification);

        return Results.Ok(new DeleteNotificationContentResponse
        {
            NotificationId = notification.Id,
            ContentDisposed = notification.ContentDisposed
        });
    }
}
