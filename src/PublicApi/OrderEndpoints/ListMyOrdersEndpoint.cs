using System;
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
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderNotificationSummaryDto
{
    public int NotificationId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int? ErrorCode { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<string> Items { get; set; } = new();
    public List<OrderNotificationSummaryDto> Notifications { get; set; } = new();
}

public class ListMyOrdersResponse : BaseResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}

/// <summary>
/// Lists the signed-in shopper's orders, each with where its notifications got to.
/// </summary>
public class ListMyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IReadRepository<Order> orderRepository,
             IReadRepository<OrderNotification> notificationRepository, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(user, orderRepository, notificationRepository, cancellationToken);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, IReadRepository<Order> orderRepository,
        IReadRepository<OrderNotification> notificationRepository, CancellationToken cancellationToken)
    {
        var orders = await orderRepository.ListAsync(
            new CustomerOrdersWithItemsSpecification(user.Identity!.Name!), cancellationToken);

        var response = new ListMyOrdersResponse();
        foreach (var order in orders.OrderByDescending(o => o.OrderDate))
        {
            var notifications = await notificationRepository.ListAsync(
                new OrderNotificationsByOrderSpecification(order.Id), cancellationToken);

            response.Orders.Add(new MyOrderDto
            {
                OrderId = order.Id,
                OrderDate = order.OrderDate,
                Status = order.Status.ToString(),
                Total = order.Total(),
                Items = order.OrderItems.Select(i => $"{i.ItemOrdered.ProductName} x{i.Units}").ToList(),
                Notifications = notifications.Select(n => new OrderNotificationSummaryDto
                {
                    NotificationId = n.Id,
                    Type = n.Type.ToString(),
                    Status = n.Status,
                    ErrorCode = n.ErrorCode,
                    SentAt = n.SentAt,
                    ScheduledFor = n.ScheduledFor
                }).ToList()
            });
        }

        return Results.Ok(response);
    }
}
