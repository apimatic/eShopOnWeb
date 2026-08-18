using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public record OrderNotificationsRequest(string BuyerId, int OrderId);

/// <summary>
/// What was sent for one of the caller's orders, and what became of each message. Scoped to the caller:
/// another shopper's order is treated as not found. Each entry carries its own notificationId.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint<IResult, OrderNotificationsRequest, IShopperOrderService, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId:int}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, IShopperOrderService orderService, IOrderNotificationService notificationService) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                return await HandleAsync(new OrderNotificationsRequest(buyerId, orderId), orderService, notificationService);
            })
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderNotificationsRequest request, IShopperOrderService orderService, IOrderNotificationService notificationService)
    {
        var order = await orderService.GetOrderForBuyerAsync(request.BuyerId, request.OrderId);
        if (order is null)
            return Results.NotFound();

        var notifications = await notificationService.GetNotificationsForOrderAsync(request.OrderId, refresh: true);
        var response = new OrderNotificationsResponse
        {
            OrderId = request.OrderId,
            Notifications = notifications.Select(NotificationDto.FromEntity).ToList()
        };
        return Results.Ok(response);
    }
}
