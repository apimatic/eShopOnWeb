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

public class OrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}

/// <summary>
/// Lists what was sent for one order and what became of each message. Each entry carries its own
/// notificationId (what the operator endpoints act on). A shopper sees only their own order; an
/// operator (admin) may view any order.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint<IResult, int, ClaimsPrincipal>
{
    private readonly IOrderMessagingService _orderMessagingService;
    private readonly IReadRepository<Order> _orderRepository;

    public OrderNotificationsEndpoint(
        IOrderMessagingService orderMessagingService,
        IReadRepository<Order> orderRepository)
    {
        _orderMessagingService = orderMessagingService;
        _orderRepository = orderRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user) => await HandleAsync(orderId, user))
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, ClaimsPrincipal user)
    {
        var buyerId = user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var isAdmin = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);

        var order = await _orderRepository.GetByIdAsync(orderId);
        // A shopper must never see another's order — hide existence with a 404.
        if (order is null || (!isAdmin && order.BuyerId != buyerId))
            return Results.NotFound();

        using var cts = EndpointBudget.Start();
        var notifications = await _orderMessagingService.GetOrderNotificationsAsync(orderId, cts.Token);
        if (notifications is null)
            return Results.NotFound();

        var response = new OrderNotificationsResponse
        {
            OrderId = orderId,
            Notifications = notifications.Select(OrderNotificationDto.From).ToList()
        };
        return Results.Ok(response);
    }
}
