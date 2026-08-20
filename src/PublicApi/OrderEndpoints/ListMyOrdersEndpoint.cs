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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersEndpoint : IEndpoint<IResult, ListMyOrdersRequest>
{
    private readonly IShopperOrderService _orders;
    private readonly IOrderNotificationService _notifications;

    public ListMyOrdersEndpoint(IShopperOrderService orders, IOrderNotificationService notifications)
    {
        _orders = orders;
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext) =>
            {
                var unauthorized = HttpCaller.UnauthorizedIfAnonymous(httpContext);
                if (unauthorized is not null)
                {
                    return unauthorized;
                }

                return await HandleAsync(new ListMyOrdersRequest
                {
                    BuyerId = HttpCaller.BuyerId(httpContext)!,
                    CancellationToken = httpContext.RequestAborted
                });
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMyOrdersRequest request)
    {
        var orders = await _orders.ListForBuyerAsync(request.BuyerId, request.CancellationToken);
        var notifications = await _notifications.ListForBuyerAsync(request.BuyerId, request.CancellationToken);
        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        var response = new ListMyOrdersResponse
        {
            Orders = orders.Select(order => new MyOrderDto
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                OrderDate = order.OrderDate,
                Total = order.Total(),
                Items = order.OrderItems.Select(i => new MyOrderItemDto
                {
                    ProductName = i.ItemOrdered.ProductName,
                    Units = i.Units,
                    UnitPrice = i.UnitPrice
                }).ToList(),
                Notifications = (byOrder.TryGetValue(order.Id, out var notes) ? notes : new List<OrderNotification>())
                    .Select(NotificationDto.From).ToList()
            }).ToList()
        };

        return Results.Ok(response);
    }
}

public class ListMyOrdersRequest : BaseRequest
{
    internal string BuyerId { get; set; } = string.Empty;
    internal CancellationToken CancellationToken { get; set; }
}

public class ListMyOrdersResponse : BaseResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public System.DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<MyOrderItemDto> Items { get; set; } = new();
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class MyOrderItemDto
{
    public string ProductName { get; set; } = string.Empty;
    public int Units { get; set; }
    public decimal UnitPrice { get; set; }
}
