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
using Microsoft.eShopWeb.PublicApi.Extensions;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// GET /api/my-orders — the caller's own orders, each showing where its notifications got to.
/// </summary>
public class MyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                ClaimsPrincipal user,
                IReadRepository<Order> orderRepository,
                IOrderNotificationService service,
                CancellationToken cancellationToken) =>
            {
                var buyerId = user.GetUserName();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var orders = await orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);

                var response = new MyOrdersResponse();
                foreach (var order in orders.OrderByDescending(o => o.OrderDate))
                {
                    var notifications = await service.GetNotificationsForOrderAsync(order.Id, refreshFromProvider: false, cancellationToken);
                    response.Orders.Add(new OrderSummaryDto
                    {
                        OrderId = order.Id,
                        OrderDate = order.OrderDate,
                        Total = order.Total(),
                        Items = order.OrderItems.Select(OrderLineDto.From).ToList(),
                        Notifications = notifications?.Select(NotificationDto.From).ToList() ?? new List<NotificationDto>()
                    });
                }

                return Results.Ok(response);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }
}
