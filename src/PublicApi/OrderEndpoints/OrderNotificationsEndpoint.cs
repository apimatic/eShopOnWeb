using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
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
/// Returns what was sent for one of the signed-in shopper's orders and what became of each message.
/// Each entry carries its own notificationId (what the operator endpoints act on). Scoped to the
/// caller's own order — another shopper's order is a 404.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint<IResult, OrderNotificationsRequest, ClaimsPrincipal>
{
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IReadRepository<Notification> _notificationRepository;
    private readonly IOrderNotificationService _orderNotificationService;

    public OrderNotificationsEndpoint(
        IReadRepository<Order> orderRepository,
        IReadRepository<Notification> notificationRepository,
        IOrderNotificationService orderNotificationService)
    {
        _orderRepository = orderRepository;
        _notificationRepository = notificationRepository;
        _orderNotificationService = orderNotificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user) =>
            {
                return await HandleAsync(new OrderNotificationsRequest(orderId), user);
            })
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderNotificationsRequest request, ClaimsPrincipal user)
    {
        var buyerId = user.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        // Only the owning shopper may see an order's notifications; not-owned and not-found look the same.
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderByIdAndBuyerSpecification(request.OrderId, buyerId));
        if (order is null)
        {
            return Results.NotFound();
        }

        await _orderNotificationService.RefreshOrderNotificationStatusesAsync(request.OrderId);

        var notifications = await _notificationRepository.ListAsync(new NotificationsByOrderSpecification(request.OrderId));
        var response = new OrderNotificationsResponse
        {
            OrderId = request.OrderId,
            Notifications = notifications.Select(NotificationDto.From).ToList()
        };
        return Results.Ok(response);
    }
}

public class OrderNotificationsRequest
{
    public OrderNotificationsRequest(int orderId)
    {
        OrderId = orderId;
    }

    public int OrderId { get; }
}

public class OrderNotificationsResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}
