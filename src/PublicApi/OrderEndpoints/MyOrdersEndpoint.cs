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
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Returns the signed-in shopper's own orders, each showing where its notifications got to.
/// </summary>
public class MyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                IRepository<Order> orderRepository,
                IOrderNotificationService notificationService,
                ClaimsPrincipal user,
                CancellationToken cancellationToken) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var orders = await orderRepository.ListAsync(new CustomerOrdersSpecification(buyerId), cancellationToken);

                var result = new MyOrdersResponse();
                foreach (var order in orders.OrderByDescending(o => o.OrderDate))
                {
                    var notifications = await notificationService.GetOrderNotificationsAsync(order.Id, cancellationToken);
                    result.Orders.Add(new MyOrderDto
                    {
                        OrderId = order.Id,
                        OrderDate = order.OrderDate,
                        Total = order.Total(),
                        Notifications = notifications.Select(NotificationDto.FromEntity).ToList()
                    });
                }

                return Results.Ok(result);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }
}
