using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrderItemDto
{
    public int CatalogItemId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public decimal UnitPrice { get; init; }
    public int Units { get; init; }
}

public class MyOrderDto
{
    public int OrderId { get; init; }
    public string Status { get; init; } = string.Empty;
    public decimal Total { get; init; }
    public System.DateTimeOffset OrderDate { get; init; }
    public List<MyOrderItemDto> Items { get; init; } = new();
    public List<NotificationDto> Notifications { get; init; } = new();
}

public class ListMyOrdersResponse
{
    public List<MyOrderDto> Orders { get; init; } = new();
}

public class ListMyOrdersEndpoint : IEndpoint<IResult, IShopOrderService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IOrderNotificationService _notifications;

    public ListMyOrdersEndpoint(IHttpContextAccessor httpContextAccessor, IOrderNotificationService notifications)
    {
        _httpContextAccessor = httpContextAccessor;
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IShopOrderService orders) =>
            {
                return await HandleAsync(orders);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(IShopOrderService orders)
    {
        var buyerId = ShopperIdentity.GetRequiredBuyerId(_httpContextAccessor.HttpContext!);
        var myOrders = await orders.ListBuyerOrdersAsync(buyerId);
        var notifications = await _notifications.ListForOrdersAsync(myOrders.Select(o => o.Id));
        var notificationsByOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        return Results.Ok(new ListMyOrdersResponse
        {
            Orders = myOrders.Select(order => new MyOrderDto
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                Total = order.Total(),
                OrderDate = order.OrderDate,
                Items = order.OrderItems.Select(i => new MyOrderItemDto
                {
                    CatalogItemId = i.ItemOrdered.CatalogItemId,
                    ProductName = i.ItemOrdered.ProductName,
                    UnitPrice = i.UnitPrice,
                    Units = i.Units
                }).ToList(),
                Notifications = notificationsByOrder.TryGetValue(order.Id, out var orderNotes)
                    ? orderNotes.Select(NotificationDto.From).ToList()
                    : new List<NotificationDto>()
            }).ToList()
        });
    }
}
