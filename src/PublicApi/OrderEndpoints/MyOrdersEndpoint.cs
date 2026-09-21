using System;
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
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}

public class MyOrdersResponse : BaseResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}

/// <summary>
/// Lists the signed-in shopper's own orders, each showing where its notifications got to (delivery
/// outcomes are refreshed from the provider, best effort, before the list is returned).
/// </summary>
public class MyOrdersEndpoint : IEndpoint<IResult, ClaimsPrincipal>
{
    private readonly IOrderMessagingService _orderMessagingService;
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IReadRepository<OrderNotification> _notificationRepository;

    public MyOrdersEndpoint(
        IOrderMessagingService orderMessagingService,
        IReadRepository<Order> orderRepository,
        IReadRepository<OrderNotification> notificationRepository)
    {
        _orderMessagingService = orderMessagingService;
        _orderRepository = orderRepository;
        _notificationRepository = notificationRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user) => await HandleAsync(user))
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user)
    {
        var buyerId = user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        using var cts = EndpointBudget.Start();
        // Refresh delivery outcomes from the provider before reporting (best effort).
        await _orderMessagingService.RefreshBuyerNotificationsAsync(buyerId, cts.Token);

        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId));
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByBuyerSpecification(buyerId));
        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        var response = new MyOrdersResponse
        {
            Orders = orders.Select(o => new MyOrderDto
            {
                OrderId = o.Id,
                OrderDate = o.OrderDate,
                Total = o.Total(),
                Notifications = byOrder.TryGetValue(o.Id, out var ns)
                    ? ns.Select(OrderNotificationDto.From).ToList()
                    : new List<OrderNotificationDto>()
            }).ToList()
        };
        return Results.Ok(response);
    }
}
