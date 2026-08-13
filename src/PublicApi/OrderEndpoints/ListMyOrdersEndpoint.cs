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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Lists the signed-in shopper's own orders, each showing where its notifications got to. Statuses
/// are refreshed against the provider before returning.
/// </summary>
public class ListMyOrdersEndpoint : IEndpoint<IResult, string, IReadRepository<Order>, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IReadRepository<Order> orderRepository, IOrderNotificationService notificationService) =>
            {
                var buyerId = user.GetUserName();
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                return await HandleAsync(buyerId, orderRepository, notificationService);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(string buyerId, IReadRepository<Order> orderRepository, IOrderNotificationService notificationService)
    {
        var orders = await orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId));
        var notifications = await notificationService.GetBuyerNotificationsAsync(buyerId, refresh: true);
        var notificationsByOrder = notifications
            .GroupBy(n => n.OrderId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var response = new ListMyOrdersResponse(Guid.NewGuid());
        foreach (var order in orders.OrderByDescending(o => o.OrderDate))
        {
            var dto = new MyOrderDto
            {
                OrderId = order.Id,
                OrderDate = order.OrderDate,
                Status = order.Status.ToString(),
                Total = order.Total(),
                Items = order.OrderItems.Select(i => new MyOrderItemDto
                {
                    ProductName = i.ItemOrdered.ProductName,
                    Units = i.Units,
                    UnitPrice = i.UnitPrice
                }).ToList()
            };

            if (notificationsByOrder.TryGetValue(order.Id, out var orderNotifications))
            {
                dto.Notifications = orderNotifications.Select(OrderNotificationDto.FromEntity).ToList();
            }

            response.Orders.Add(dto);
        }

        return Results.Ok(response);
    }
}

public class ListMyOrdersResponse : BaseResponse
{
    public ListMyOrdersResponse(Guid correlationId) : base(correlationId) { }
    public ListMyOrdersResponse() { }

    public List<MyOrderDto> Orders { get; set; } = new();
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<MyOrderItemDto> Items { get; set; } = new();
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}

public class MyOrderItemDto
{
    public string ProductName { get; set; } = string.Empty;
    public int Units { get; set; }
    public decimal UnitPrice { get; set; }
}
