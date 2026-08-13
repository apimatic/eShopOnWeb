using System;
using System.Collections.Generic;
using System.Linq;
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

namespace Microsoft.eShopWeb.PublicApi.SmsNotifications.OrderEndpoints;

public class MyOrderSummaryDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = string.Empty;

    /// <summary>Each notification for this order, showing where it got to.</summary>
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class MyOrdersResponse : BaseResponse
{
    public List<MyOrderSummaryDto> Orders { get; set; } = new();
}

/// <summary>
/// Returns the caller's own orders, each showing where its notifications got to. Delivery outcomes
/// are refreshed from the provider (there is no callback into this app) before they are returned.
/// </summary>
public class MyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                IReadRepository<Order> orderRepository,
                IRepository<OrderNotification> notificationRepository,
                IOrderNotificationService notificationService,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                var buyerId = CallerIdentity.GetBuyerId(httpContext);
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                var orders = await orderRepository.ListAsync(
                    new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);

                var response = new MyOrdersResponse();
                foreach (var order in orders.OrderByDescending(o => o.OrderDate))
                {
                    var notifications = await notificationRepository.ListAsync(
                        new OrderNotificationsByOrderSpecification(order.Id), cancellationToken);

                    foreach (var notification in notifications)
                        await notificationService.RefreshDeliveryStateAsync(notification, cancellationToken);

                    response.Orders.Add(new MyOrderSummaryDto
                    {
                        OrderId = order.Id,
                        OrderDate = order.OrderDate,
                        Total = order.Total(),
                        Status = order.Status.ToString(),
                        Notifications = notifications.Select(NotificationDto.FromEntity).ToList()
                    });
                }

                return Results.Ok(response);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }
}
