using System;
using System.Collections.Generic;
using System.Linq;
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
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Lists the signed-in shopper's orders, each showing where its notifications got to. Delivery
/// outcomes are refreshed from the provider before returning.
/// </summary>
public class MyOrdersEndpoint : IEndpoint<IResult, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext) =>
            {
                return await HandleAsync(httpContext);
            })
            .Produces<MyOrdersResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext httpContext)
    {
        var cancellationToken = httpContext.RequestAborted;
        var buyerId = httpContext.User.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var orderRepository = httpContext.RequestServices.GetRequiredService<IReadRepository<Order>>();
        var notificationRepository = httpContext.RequestServices.GetRequiredService<IRepository<OrderNotification>>();
        var notificationService = httpContext.RequestServices.GetRequiredService<IOrderNotificationService>();

        var orders = await orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var notifications = await notificationRepository.ListAsync(new OrderNotificationsByBuyerSpecification(buyerId), cancellationToken);

        // Pull the latest delivery outcomes from the provider (there is no inbound webhook).
        await notificationService.RefreshDeliveryStatusesAsync(notifications, cancellationToken);

        var notificationsByOrder = notifications
            .GroupBy(n => n.OrderId)
            .ToDictionary(g => g.Key, g => g.Select(NotificationDto.From).ToList());

        var response = new MyOrdersResponse
        {
            Orders = orders
                .OrderByDescending(o => o.Id)
                .Select(o => new OrderSummaryDto
                {
                    OrderId = o.Id,
                    OrderDate = o.OrderDate,
                    Status = o.Status.ToString(),
                    Total = o.Total(),
                    Items = o.OrderItems.Select(i => new OrderLineDto
                    {
                        CatalogItemId = i.ItemOrdered.CatalogItemId,
                        ProductName = i.ItemOrdered.ProductName,
                        UnitPrice = i.UnitPrice,
                        Units = i.Units
                    }).ToList(),
                    Notifications = notificationsByOrder.TryGetValue(o.Id, out var list) ? list : new List<NotificationDto>()
                })
                .ToList()
        };
        return Results.Ok(response);
    }
}

public class MyOrdersResponse : BaseResponse
{
    public List<OrderSummaryDto> Orders { get; set; } = new();
}

public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<OrderLineDto> Items { get; set; } = new();
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}
