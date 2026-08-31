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
/// Disposes of a message's content (operator, on a shopper's request). The text is erased
/// at the provider, not merely hidden here; the record of the send and its outcome survive.
/// </summary>
public class DeleteNotificationContentEndpoint : IEndpoint<IResult, DeleteNotificationContentRequest, IRepository<OrderNotification>, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IRepository<OrderNotification> notificationRepository, IOrderNotificationService notificationService) =>
            {
                return await HandleAsync(new DeleteNotificationContentRequest(notificationId), notificationRepository, notificationService);
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteNotificationContentRequest request, IRepository<OrderNotification> notificationRepository, IOrderNotificationService notificationService)
    {
        var notification = await notificationRepository.GetByIdAsync(request.NotificationId);
        if (notification == null)
        {
            return Results.NotFound();
        }

        if (notification.ContentRedacted)
        {
            return Results.NoContent();
        }

        try
        {
            await notificationService.RedactContentAsync(notification);
            return Results.NoContent();
        }
        catch (SmsProviderException ex)
        {
            return ProviderErrorResults.Map(ex);
        }
    }
}
