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
/// Shows what was sent for one of the caller's own orders, and what became of each message. Each
/// entry carries its own <c>notificationId</c> — the identifier the operator endpoints act on.
/// </summary>
public class GetOrderNotificationsEndpoint : IEndpoint<IResult, int, HttpContext>
{
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IOrderNotificationService _orderNotificationService;

    public GetOrderNotificationsEndpoint(IReadRepository<Order> orderRepository, IOrderNotificationService orderNotificationService)
    {
        _orderRepository = orderRepository;
        _orderNotificationService = orderNotificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext http) => await HandleAsync(orderId, http))
            .Produces<OrderNotificationsResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, HttpContext http)
    {
        var buyerId = http.User.GetUserName();
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var ct = http.RequestAborted;

        // The order must exist and belong to the caller; otherwise it is not theirs to see.
        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null || order.BuyerId != buyerId)
            return Results.NotFound();

        var notifications = await _orderNotificationService.GetOrderNotificationsAsync(orderId, ct);

        var response = new OrderNotificationsResponse
        {
            OrderId = orderId,
            Notifications = notifications.Select(NotificationDto.FromEntity).ToList()
        };
        return Results.Ok(response);
    }
}

public class OrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}
