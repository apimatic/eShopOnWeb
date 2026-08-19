using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Lists what was sent for one of the signed-in shopper's orders and what became of each message.
/// Each entry carries its own notificationId — what the operator endpoints act on. Owner-scoped.
/// </summary>
public class ListOrderNotificationsEndpoint : IEndpoint<IResult, int, IOrderNotificationService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderNotificationService service, ClaimsPrincipal user) =>
            {
                return await HandleAsync(orderId, service, user);
            })
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IOrderNotificationService service, ClaimsPrincipal user)
    {
        var notifications = await service.GetOrderNotificationsAsync(orderId, user.GetOwnerId());
        if (notifications is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new OrderNotificationsResponse
        {
            OrderId = orderId,
            Notifications = NotificationDto.FromViews(notifications)
        });
    }
}
