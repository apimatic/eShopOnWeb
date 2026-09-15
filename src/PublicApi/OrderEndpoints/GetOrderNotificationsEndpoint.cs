using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// What was sent for this order and what became of each message. Shopper-scoped: the caller must own
/// the order. Each entry carries its own notificationId — what the operator endpoints act on.
/// </summary>
public class GetOrderNotificationsEndpoint : IEndpoint<IResult, IRepository<Order>, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, System.Security.Claims.ClaimsPrincipal user, IRepository<Order> orderRepository,
                IOrderNotificationService notifications) =>
            {
                var owner = CallerIdentity.GetUserName(user);
                if (string.IsNullOrEmpty(owner))
                {
                    return Results.Unauthorized();
                }

                // A shopper only sees their own order's notifications; another's order is simply "not found".
                var order = await orderRepository.GetByIdAsync(orderId);
                if (order is null || order.BuyerId != owner)
                {
                    return Results.NotFound();
                }

                var list = await notifications.GetOrderNotificationsAsync(orderId);
                var response = new OrderNotificationsResponse
                {
                    OrderId = orderId,
                    Notifications = list.Select(NotificationDto.From).ToList()
                };
                return Results.Ok(response);
            })
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(IRepository<Order> orderRepository, IOrderNotificationService notifications) =>
        Task.FromResult<IResult>(Results.Empty);
}

public class OrderNotificationsResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}
