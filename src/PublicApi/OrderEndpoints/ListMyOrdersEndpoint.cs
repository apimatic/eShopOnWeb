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

public class ListMyOrdersRequest : BaseRequest
{
}

public class MyOrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Units { get; set; }
    public decimal UnitPrice { get; set; }
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<MyOrderItemDto> Items { get; set; } = new();
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class ListMyOrdersResponse : BaseResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}

public class ListMyOrdersEndpoint : IEndpoint<IResult, ListMyOrdersRequest, IOrderFulfillmentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, IOrderFulfillmentService fulfillmentService) =>
            {
                var buyerId = EndpointIdentity.GetBuyerId(httpContext);
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var orders = await fulfillmentService.ListMyOrdersAsync(buyerId, httpContext.RequestAborted);
                var response = new ListMyOrdersResponse
                {
                    Orders = orders.Select(view => new MyOrderDto
                    {
                        OrderId = view.Order.Id,
                        Status = view.Order.Status.ToString(),
                        OrderDate = view.Order.OrderDate,
                        Total = view.Order.Total(),
                        Items = view.Order.OrderItems.Select(i => new MyOrderItemDto
                        {
                            CatalogItemId = i.ItemOrdered.CatalogItemId,
                            ProductName = i.ItemOrdered.ProductName,
                            Units = i.Units,
                            UnitPrice = i.UnitPrice
                        }).ToList(),
                        Notifications = view.Notifications.Select(NotificationMapper.ToDto).Where(d => d != null).Cast<NotificationDto>().ToList()
                    }).ToList()
                };
                return Results.Ok(response);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(ListMyOrdersRequest request, IOrderFulfillmentService fulfillmentService)
        => Task.FromResult(Results.Ok());
}
