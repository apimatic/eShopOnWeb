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
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// What was sent for one of the signed-in shopper's orders, and what became of each message.
/// </summary>
public class ListOrderNotificationsEndpoint : IEndpoint<IResult, int, ClaimsPrincipal>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly INotificationService _notificationService;

    public ListOrderNotificationsEndpoint(IRepository<Order> orderRepository, INotificationService notificationService)
    {
        _orderRepository = orderRepository;
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user) =>
            {
                return await HandleAsync(orderId, user);
            })
            .Produces<ListOrderNotificationsResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var order = await _orderRepository.GetByIdAsync(orderId);
        if (order == null || order.BuyerId != buyerId)
        {
            return Results.NotFound();
        }

        var notifications = await _notificationService.GetOrderNotificationsAsync(orderId, refreshStatus: true);

        var response = new ListOrderNotificationsResponse
        {
            OrderId = order.Id,
            OrderStatus = order.Status.ToString()
        };
        response.Notifications.AddRange(notifications.Select(OrderNotificationDto.FromEntity));
        return Results.Ok(response);
    }
}

public class ListOrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}
