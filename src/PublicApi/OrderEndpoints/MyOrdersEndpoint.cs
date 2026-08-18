using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Lists the signed-in shopper's own orders, each showing where its notifications got to. A shopper never
/// sees another's orders.
/// </summary>
public class MyOrdersEndpoint : IEndpoint<IResult, ClaimsPrincipal>
{
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IReadRepository<OrderNotification> _notificationRepository;

    public MyOrdersEndpoint(IReadRepository<Order> orderRepository, IReadRepository<OrderNotification> notificationRepository)
    {
        _orderRepository = orderRepository;
        _notificationRepository = notificationRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user) =>
            {
                return await HandleAsync(user);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user)
    {
        var buyerId = user.CallerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var orders = await _orderRepository.ListAsync(new OrdersByBuyerSpecification(buyerId));
        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByBuyerSpecification(buyerId));
        var notificationsByOrder = notifications
            .GroupBy(n => n.OrderId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var response = new MyOrdersResponse();
        foreach (var order in orders)
        {
            var summary = new OrderSummaryDto
            {
                OrderId = order.Id,
                OrderDate = order.OrderDate,
                Status = order.Status.ToString(),
                Total = order.Total(),
                Items = order.OrderItems.Select(i => new OrderItemDto
                {
                    CatalogItemId = i.ItemOrdered.CatalogItemId,
                    ProductName = i.ItemOrdered.ProductName,
                    UnitPrice = i.UnitPrice,
                    Units = i.Units
                }).ToList(),
                Notifications = notificationsByOrder.TryGetValue(order.Id, out var orderNotifications)
                    ? orderNotifications.Select(NotificationDto.From).ToList()
                    : new List<NotificationDto>()
            };
            response.Orders.Add(summary);
        }

        return Results.Ok(response);
    }
}
