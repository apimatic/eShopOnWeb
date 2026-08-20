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

public class ListMyOrdersEndpoint : IEndpoint<IResult, HttpContext, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext http, IShopperOrderService orders) => await HandleAsync(http, orders))
            .Produces<Response>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext http, IShopperOrderService orders)
    {
        var buyerId = http.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var myOrders = await orders.ListMyOrdersAsync(buyerId);
        var notifications = await http.GetRequired<IOrderNotificationService>()
            .ListForOrdersAsync(myOrders.Select(o => o.Order.Id));
        var byOrder = notifications.GroupBy(n => n.ForOrderId).ToDictionary(g => g.Key, g => g.ToList());

        return Results.Ok(new Response
        {
            Orders = myOrders.Select(shopperOrder => new OrderSummary
            {
                OrderId = shopperOrder.Order.Id,
                Status = shopperOrder.Status.ToString(),
                OrderDate = shopperOrder.Order.OrderDate,
                Total = shopperOrder.Order.Total(),
                Items = shopperOrder.Order.OrderItems.Select(i => new OrderItemSummary
                {
                    CatalogItemId = i.ItemOrdered.CatalogItemId,
                    ProductName = i.ItemOrdered.ProductName,
                    Quantity = i.Units,
                    UnitPrice = i.UnitPrice
                }).ToList(),
                Notifications = (byOrder.TryGetValue(shopperOrder.Order.Id, out var list) ? list : new())
                    .Select(NotificationDto.From)
                    .ToList()
            }).ToList()
        });
    }

    public class Response
    {
        public List<OrderSummary> Orders { get; set; } = new();
    }

    public class OrderSummary
    {
        public int OrderId { get; set; }
        public string Status { get; set; } = string.Empty;
        public System.DateTimeOffset OrderDate { get; set; }
        public decimal Total { get; set; }
        public List<OrderItemSummary> Items { get; set; } = new();
        public List<NotificationDto> Notifications { get; set; } = new();
    }

    public class OrderItemSummary
    {
        public int CatalogItemId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
    }
}
