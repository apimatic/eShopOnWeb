using System;
using System.Collections.Generic;
using System.Linq;
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

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public class MyOrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<NotificationStatusDto> Notifications { get; set; } = new();
}

public class MyOrdersResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}

/// <summary>Lists the signed-in shopper's own orders, each showing where its notifications got to.</summary>
public class MyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, IReadRepository<Order> orderRepository,
             IReadRepository<OrderNotification> notificationRepository, System.Threading.CancellationToken ct) =>
            {
                var buyerId = CallerContext.GetUserName(http);
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

                var orders = await orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
                var notifications = await notificationRepository.ListAsync(new OrderNotificationsByOwnerSpecification(buyerId), ct);
                var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());

                var response = new MyOrdersResponse
                {
                    Orders = orders.Select(o => new MyOrderDto
                    {
                        OrderId = o.Id,
                        Status = o.Status.ToString(),
                        OrderDate = o.OrderDate,
                        Total = o.Total(),
                        Notifications = byOrder.TryGetValue(o.Id, out var ns)
                            ? ns.Select(NotificationStatusDto.From).ToList()
                            : new List<NotificationStatusDto>()
                    }).ToList()
                };
                return Results.Ok(response);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderNotificationEndpoints");
    }
}
