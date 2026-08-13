using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Returns what was sent for one of the caller's own orders, and what became of each message. Each
/// entry carries its own notificationId — the identifier the operator endpoints act on.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, IReadRepository<Order> orderRepository, INotificationService notifications) =>
            {
                var buyerId = user.FindFirstValue(ClaimTypes.Name);
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                var order = await orderRepository.GetByIdAsync(orderId);
                // The order must exist and belong to the caller — reveal nothing otherwise.
                if (order is null || order.BuyerId != buyerId)
                    return Results.NotFound();

                var orderNotifications = await notifications.GetOrderNotificationsAsync(orderId);
                var response = new OrderNotificationsResponse
                {
                    OrderId = orderId,
                    Notifications = orderNotifications.OrderBy(n => n.Id).Select(SmsNotificationDto.From).ToList()
                };
                return Results.Ok(response);
            })
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }
}

public class OrderNotificationsResponse
{
    public int OrderId { get; set; }
    public List<SmsNotificationDto> Notifications { get; set; } = new();
}
