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
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Returns the signed-in shopper's own orders, each showing where its notifications got to (their
/// current delivery outcomes, refreshed from the provider).
/// </summary>
public class MyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IReadRepository<Order> orderRepository,
             IRepository<SmsNotification> notificationRepository, INotificationService notifications) =>
            {
                var buyerId = user.FindFirstValue(ClaimTypes.Name);
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                var orders = await orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId));

                var myNotifications = await notificationRepository.ListAsync(new NotificationsByBuyerSpecification(buyerId));
                await notifications.RefreshStatusesAsync(myNotifications);
                var byOrder = myNotifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());

                var response = new MyOrdersResponse
                {
                    Orders = orders.OrderByDescending(o => o.Id).Select(o => new MyOrderDto
                    {
                        OrderId = o.Id,
                        Status = o.Status.ToString(),
                        OrderDate = o.OrderDate,
                        Total = o.Total(),
                        Notifications = (byOrder.TryGetValue(o.Id, out var list) ? list : new List<SmsNotification>())
                            .OrderBy(n => n.Id).Select(SmsNotificationDto.From).ToList()
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
    public List<SmsNotificationDto> Notifications { get; set; } = new();
}
