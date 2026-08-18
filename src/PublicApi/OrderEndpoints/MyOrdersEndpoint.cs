using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>GET /api/my-orders — the caller's orders, each showing where its notifications got to.</summary>
public class MyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                IReadRepository<Order> orderRepository,
                IReadRepository<OrderNotification> notificationRepository,
                ClaimsPrincipal user,
                CancellationToken ct) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var orders = await orderRepository.ListAsync(new CustomerOrdersSpecification(buyerId), ct);
                var notifications = await notificationRepository.ListAsync(new OrderNotificationsByBuyerSpecification(buyerId), ct);
                var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());

                var response = new MyOrdersResponse
                {
                    Orders = orders.Select(o => new MyOrderDto
                    {
                        OrderId = o.Id,
                        OrderDate = o.OrderDate,
                        Total = o.Total(),
                        Items = o.OrderItems.Select(i => new MyOrderItemDto
                        {
                            CatalogItemId = i.ItemOrdered.CatalogItemId,
                            ProductName = i.ItemOrdered.ProductName,
                            UnitPrice = i.UnitPrice,
                            Units = i.Units
                        }).ToList(),
                        Notifications = (byOrder.TryGetValue(o.Id, out var list) ? list : new List<OrderNotification>())
                            .Select(NotificationDto.From).ToList()
                    }).ToList()
                };

                return Results.Ok(response);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }
}
