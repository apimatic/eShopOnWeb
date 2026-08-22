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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersEndpoint : IEndpoint<IResult, ListMyOrdersRequest, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, IShopperOrderService shopperOrderService) =>
            {
                return await HandleAsync(new ListMyOrdersRequest { BuyerId = httpContext.GetBuyerId() }, shopperOrderService);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMyOrdersRequest request, IShopperOrderService shopperOrderService)
    {
        var orders = await shopperOrderService.GetMyOrdersAsync(request.BuyerId);
        var response = new ListMyOrdersResponse(request.CorrelationId())
        {
            Orders = orders.Select(o => new MyOrderDto
            {
                OrderId = o.Order.Id,
                Status = o.Order.Status.ToString(),
                OrderDate = o.Order.OrderDate,
                Total = o.Order.Total(),
                Items = o.Order.OrderItems.Select(i => new MyOrderItemDto
                {
                    CatalogItemId = i.ItemOrdered.CatalogItemId,
                    ProductName = i.ItemOrdered.ProductName,
                    UnitPrice = i.UnitPrice,
                    Units = i.Units
                }).ToList(),
                Notifications = o.Notifications.Select(OrderNotificationDto.From).ToList()
            }).ToList()
        };

        return Results.Ok(response);
    }
}

public class ListMyOrdersRequest : BaseRequest
{
    public string BuyerId { get; set; } = string.Empty;
}

public class ListMyOrdersResponse : BaseResponse
{
    public ListMyOrdersResponse(Guid correlationId) : base(correlationId)
    {
    }

    public List<MyOrderDto> Orders { get; set; } = new();
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<MyOrderItemDto> Items { get; set; } = new();
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}

public class MyOrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}
