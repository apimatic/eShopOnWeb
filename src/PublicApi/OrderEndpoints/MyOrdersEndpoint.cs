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
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// The signed-in shopper's own orders, each showing where its notifications got to. Delivery outcomes are
/// refreshed from the provider on read (there is no callback URL into this app).
/// </summary>
public class MyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                ClaimsPrincipal user,
                IReadRepository<Order> orderRepository,
                IRepository<Notification> notificationRepository,
                IOrderNotificationService notificationService,
                CancellationToken cancellationToken) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var orders = await orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
                var notifications = await notificationRepository.ListAsync(new NotificationsByBuyerSpecification(buyerId), cancellationToken);

                // Bring each non-terminal message's delivery outcome up to date from the provider.
                await notificationService.RefreshStatusesAsync(notifications, cancellationToken);

                var byOrder = notifications
                    .GroupBy(n => n.OrderId)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(n => n.CreatedDate).ToList());

                var summaries = orders.Select(o => new OrderSummaryDto
                {
                    OrderId = o.Id,
                    Status = o.Status.ToString(),
                    OrderDate = o.OrderDate,
                    Total = o.Total(),
                    Notifications = byOrder.TryGetValue(o.Id, out var ns)
                        ? ns.Select(NotificationDto.From).ToList()
                        : new List<NotificationDto>()
                }).ToList();

                return Results.Ok(summaries);
            })
            .Produces<List<OrderSummaryDto>>()
            .WithTags("OrderEndpoints");
    }
}
