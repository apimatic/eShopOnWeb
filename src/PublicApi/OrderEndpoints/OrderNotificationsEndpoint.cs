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
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderNotificationsResponse
{
    public int OrderId { get; set; }

    /// <summary>Each entry carries its own notificationId — that is what the operator endpoints act on.</summary>
    public List<NotificationDto> Notifications { get; set; } = new();
}

/// <summary>
/// Returns what was sent for one of the caller's orders and what became of each message. Scoped to the
/// caller: a shopper can only see notifications for an order they own.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint<IResult, int, IReadRepository<Order>, IOrderNotificationService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public OrderNotificationsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IReadRepository<Order> orderRepository, IOrderNotificationService notificationService) =>
                await HandleAsync(orderId, orderRepository, notificationService))
            .Produces<OrderNotificationsResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IReadRepository<Order> orderRepository, IOrderNotificationService notificationService)
    {
        var buyerId = _httpContextAccessor.GetUserName();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var ct = _httpContextAccessor.RequestAborted();

        var order = await orderRepository.GetByIdAsync(orderId, ct);

        // Not found, or not the caller's order — either way a 404 (don't reveal another shopper's order exists).
        if (order is null || order.BuyerId != buyerId)
        {
            return Results.NotFound();
        }

        var notifications = await notificationService.GetOrderNotificationsAsync(orderId, ct);

        var response = new OrderNotificationsResponse
        {
            OrderId = orderId,
            Notifications = notifications.Select(NotificationDto.From).ToList()
        };

        return Results.Ok(response);
    }
}
