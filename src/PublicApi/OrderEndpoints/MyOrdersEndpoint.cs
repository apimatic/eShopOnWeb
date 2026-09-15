using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<MyOrderItemDto> Items { get; set; } = new();

    /// <summary>Where this order's notifications got to (last-known outcome).</summary>
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class MyOrdersResponse : BaseResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}

/// <summary>
/// GET /api/my-orders — the signed-in shopper's own orders, each showing where its notifications got
/// to. Shopper-scoped: only the caller's orders are returned.
/// </summary>
public class MyOrdersEndpoint : ApiEndpointBase,
    IEndpoint<IResult, IApiOrderService, INotificationService>
{
    public MyOrdersEndpoint(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor) { }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IApiOrderService orderService, INotificationService notificationService) =>
                await HandleAsync(orderService, notificationService))
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(IApiOrderService orderService, INotificationService notificationService)
    {
        var buyerId = CallerId;
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var orders = await orderService.GetOrdersForBuyerAsync(buyerId, Aborted);
        var orderIds = orders.Select(o => o.Id).ToList();
        var notifications = await notificationService.GetNotificationsForOrdersAsync(orderIds, Aborted);
        var notificationsByOrder = notifications
            .GroupBy(n => n.OrderId)
            .ToDictionary(g => g.Key, g => g.Select(NotificationDto.From).ToList());

        var response = new MyOrdersResponse
        {
            Orders = orders.Select(o => new MyOrderDto
            {
                OrderId = o.Id,
                Status = o.Status.ToString(),
                OrderDate = o.OrderDate,
                Total = o.Total(),
                Items = o.OrderItems.Select(i => new MyOrderItemDto
                {
                    CatalogItemId = i.ItemOrdered.CatalogItemId,
                    ProductName = i.ItemOrdered.ProductName,
                    UnitPrice = i.UnitPrice,
                    Units = i.Units
                }).ToList(),
                Notifications = notificationsByOrder.TryGetValue(o.Id, out var dtos) ? dtos : new List<NotificationDto>()
            }).ToList()
        };
        return Results.Ok(response);
    }
}
