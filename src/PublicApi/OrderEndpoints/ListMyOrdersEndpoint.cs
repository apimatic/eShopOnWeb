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
using Microsoft.eShopWeb.PublicApi.SmsNotifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// The signed-in shopper's own orders, each showing where its notifications got to.
/// </summary>
public class ListMyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                ClaimsPrincipal user,
                IRepository<Order> orderRepository,
                IOrderNotificationService notifications) =>
            {
                var ownerId = user.GetOwnerId();
                if (string.IsNullOrEmpty(ownerId))
                {
                    return Results.Unauthorized();
                }

                var orders = await orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(ownerId));
                var ownerNotifications = await notifications.GetNotificationsForOwnerAsync(ownerId);
                var byOrder = ownerNotifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());

                var response = new MyOrdersResponse
                {
                    Orders = orders.Select(o => new MyOrderDto
                    {
                        OrderId = o.Id,
                        Status = o.Status.ToString(),
                        OrderDate = o.OrderDate,
                        Total = o.Total(),
                        Notifications = (byOrder.TryGetValue(o.Id, out var list) ? list : new())
                            .Select(n => n.ToView())
                            .ToList()
                    }).ToList()
                };

                return Results.Ok(response);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }
}

public class MyOrdersResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<NotificationView> Notifications { get; set; } = new();
}
