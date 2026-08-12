using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Lists what was sent for one of the caller's orders, and what became of each message. Each entry
/// carries its own notificationId, which is what the operator endpoints act on.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint<IResult, OrderNotificationsEndpoint.Request, IRepository<OrderNotification>>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IOrderNotificationService _notifications;

    public record Request(int OrderId, string BuyerId);

    public OrderNotificationsEndpoint(IRepository<Order> orderRepository, IOrderNotificationService notifications)
    {
        _orderRepository = orderRepository;
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId:int}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, IRepository<OrderNotification> notificationRepository) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(new Request(orderId, buyerId), notificationRepository);
            })
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(Request request, IRepository<OrderNotification> notificationRepository)
    {
        // The order (and therefore its notifications) must belong to the caller.
        var order = await _orderRepository.GetByIdAsync(request.OrderId);
        if (order is null || order.BuyerId != request.BuyerId)
        {
            return Results.NotFound();
        }

        var notifications = await notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(request.OrderId));
        await _notifications.RefreshDeliveryStatesAsync(notifications, CancellationToken.None);

        var response = new OrderNotificationsResponse
        {
            OrderId = request.OrderId,
            Notifications = notifications.Select(NotificationDto.From).ToList()
        };
        return Results.Ok(response);
    }
}
