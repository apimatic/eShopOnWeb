using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Disposes of a message's content (operator, on a shopper's request). The text is erased
/// at the provider as well as locally; the record that the message was sent, and its
/// outcome, survives.
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
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteNotificationContentRequest request,
        IRepository<OrderNotification> notificationRepository, IOrderNotificationService notificationService)
    {
        var notification = await notificationRepository.GetByIdAsync(request.NotificationId);
        if (notification is null)
        {
            return Results.NotFound();
        }

        var redacted = await notificationService.RedactContentAsync(notification);
        if (!redacted)
        {
            return Results.Problem(
                "The provider could not confirm disposal of the message content. Nothing was changed; try again later.",
                statusCode: StatusCodes.Status502BadGateway);
        }

        return Results.NoContent();
    }
}
