using System.Linq;
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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/cancel — operator cancels the order. The shopper is told, and any
/// not-yet-sent delivery follow-up is called off with the provider so it can never reach them.
/// Operator-only.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, int, HttpContext, IRepository<Order>>
{
    private readonly IOrderNotificationService _orderNotifications;
    private readonly IReadRepository<OrderNotification> _notificationsRead;

    public CancelOrderEndpoint(IOrderNotificationService orderNotifications, IReadRepository<OrderNotification> notificationsRead)
    {
        _orderNotifications = orderNotifications;
        _notificationsRead = notificationsRead;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext http, IRepository<Order> orderRepository) =>
            {
                return await HandleAsync(orderId, http, orderRepository);
            })
            .Produces<OrderOperationResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, HttpContext http, IRepository<Order> orderRepository)
    {
        var order = await orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), http.RequestAborted);
        if (order is null) return Results.NotFound();

        if (order.Status == OrderStatus.Cancelled)
        {
            return Results.Conflict(new { error = $"Order {orderId} is already cancelled." });
        }

        order.MarkCancelled();
        await orderRepository.UpdateAsync(order, http.RequestAborted);

        // Calls off any scheduled follow-up first, then tells the shopper. Best-effort messaging.
        await _orderNotifications.NotifyOrderCancelledAsync(order, http.RequestAborted);

        var notifications = await _notificationsRead.ListAsync(new OrderNotificationsByOrderSpecification(orderId), http.RequestAborted);
        return Results.Ok(new OrderOperationResponse
        {
            OrderId = orderId,
            Status = order.Status.ToString(),
            Notifications = notifications.Select(OrderNotificationDto.From).ToList()
        });
    }
}
