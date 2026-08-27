using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class ListMyOrdersResponse : BaseResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}

/// <summary>
/// Lists the signed-in shopper's orders, each with where its notifications got to.
/// Notification outcomes are refreshed from the provider on read.
/// </summary>
public class ListMyOrdersEndpoint : IEndpoint<IResult, HttpContext>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Notification> _notificationRepository;
    private readonly IOrderNotificationService _orderNotificationService;

    public ListMyOrdersEndpoint(
        IRepository<Order> orderRepository,
        IRepository<Notification> notificationRepository,
        IOrderNotificationService orderNotificationService)
    {
        _orderRepository = orderRepository;
        _notificationRepository = notificationRepository;
        _orderNotificationService = orderNotificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext) =>
            {
                return await HandleAsync(httpContext);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext httpContext)
    {
        var buyerId = httpContext.User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        await _orderNotificationService.RefreshBuyerNotificationsAsync(buyerId);

        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId));
        var notifications = await _notificationRepository.ListAsync(new NotificationsByBuyerSpecification(buyerId));
        var notificationsByOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        var response = new ListMyOrdersResponse
        {
            Orders = orders.Select(o => new MyOrderDto
            {
                OrderId = o.Id,
                OrderDate = o.OrderDate,
                Status = o.Status.ToString(),
                Total = o.Total(),
                Notifications = notificationsByOrder.TryGetValue(o.Id, out var orderNotifications)
                    ? orderNotifications.OrderBy(n => n.CreatedAt).Select(NotificationDto.FromEntity).ToList()
                    : new List<NotificationDto>()
            }).OrderByDescending(o => o.OrderDate).ToList()
        };
        return Results.Ok(response);
    }
}
