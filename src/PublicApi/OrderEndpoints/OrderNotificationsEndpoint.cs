using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// What was sent for this order and what became of each message. Shopper-scoped: only the order's
/// owner can see it, and each entry carries its own notificationId (what the operator endpoints act on).
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint<IResult, int, IOrderNotificationService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderNotificationService notificationService, HttpContext http) =>
            {
                return await HandleAsync(orderId, notificationService, http);
            })
            .Produces<OrderNotificationsResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IOrderNotificationService notificationService, HttpContext http)
    {
        var buyerId = http.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        // Enforce ownership: a shopper only sees notifications for their own order.
        var orderService = http.RequestServices.GetRequiredService<IStoreOrderService>();
        var order = await orderService.GetOrderForBuyerAsync(orderId, buyerId, http.RequestAborted);
        if (order is null)
            return Results.NotFound();

        var notifications = await notificationService.GetOrderNotificationsAsync(orderId, http.RequestAborted);
        var response = new OrderNotificationsResponse
        {
            OrderId = orderId,
            Notifications = notifications.Select(OrderNotificationDto.FromEntity).ToList()
        };
        return Results.Ok(response);
    }
}
