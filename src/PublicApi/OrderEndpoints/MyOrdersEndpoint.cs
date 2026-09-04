using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// The signed-in shopper's own orders with their payment state.
/// </summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersResponse, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IOrderPaymentService orderPaymentService, HttpContext http, CancellationToken ct) =>
            {
                return await HandleAsync(new MyOrdersResponse(Guid.NewGuid()), orderPaymentService, http, ct);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(MyOrdersResponse request, IOrderPaymentService orderPaymentService) =>
        HandleAsync(request, orderPaymentService, httpContext: null, CancellationToken.None);

    public async Task<IResult> HandleAsync(MyOrdersResponse request, IOrderPaymentService orderPaymentService, HttpContext? httpContext, CancellationToken ct)
    {
        var buyerId = httpContext?.User?.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var orders = await orderPaymentService.ListMyOrdersAsync(buyerId, ct);
        return Results.Ok(new MyOrdersResponse(request.CorrelationId())
        {
            Orders = orders.Select(o => new OrderDto
            {
                OrderId = o.Id,
                OrderDate = o.OrderDate,
                Status = o.Status.ToString(),
                Total = o.Total(),
                Currency = o.Payment?.Currency,
                Items = o.OrderItems.Select(oi => new OrderItemDto
                {
                    CatalogItemId = oi.ItemOrdered.CatalogItemId,
                    ProductName = oi.ItemOrdered.ProductName,
                    Quantity = oi.Units,
                    UnitPrice = oi.UnitPrice
                }).ToList(),
                Payment = PayOrderEndpoint.ToPaymentState(o)
            }).ToList()
        });
    }
}
