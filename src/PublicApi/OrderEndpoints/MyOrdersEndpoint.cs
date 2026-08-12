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
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.NotificationsFeature;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrdersResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}

/// <summary>
/// GET /api/my-orders — the caller's own orders, each showing where its notifications got to.
/// Delivery outcomes are refreshed from the provider before reporting.
/// </summary>
public class MyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                ClaimsPrincipal user,
                IReadRepository<OrderStatusRecord> statusRepository,
                IRepository<OrderNotification> notificationRepository,
                IReadRepository<Order> orderRepository,
                IOrderNotificationService notificationService,
                CancellationToken cancellationToken) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrWhiteSpace(buyerId))
                    return Results.Unauthorized();

                var statusRecords = await statusRepository.ListAsync(
                    new OrderStatusRecordsByBuyerSpecification(buyerId), cancellationToken);
                var notifications = await notificationRepository.ListAsync(
                    new OrderNotificationsByBuyerSpecification(buyerId), cancellationToken);

                // Bring stored delivery outcomes up to date with the provider before reporting.
                await notificationService.RefreshStatusesAsync(notifications, cancellationToken);

                var orders = await orderRepository.ListAsync(
                    new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
                var orderById = orders.ToDictionary(o => o.Id);
                var notificationsByOrder = notifications
                    .GroupBy(n => n.OrderId)
                    .ToDictionary(g => g.Key, g => g.OrderBy(n => n.CreatedAt).ToList());

                var response = new MyOrdersResponse
                {
                    Orders = statusRecords.Select(record =>
                    {
                        orderById.TryGetValue(record.OrderId, out var order);
                        return new MyOrderDto
                        {
                            OrderId = record.OrderId,
                            State = record.State.ToString(),
                            OrderDate = order?.OrderDate ?? record.CreatedAt,
                            Total = order?.Total() ?? 0m,
                            Notifications = notificationsByOrder.TryGetValue(record.OrderId, out var list)
                                ? list.Select(NotificationDto.From).ToList()
                                : new List<NotificationDto>()
                        };
                    }).ToList()
                };
                return Results.Ok(response);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }
}
