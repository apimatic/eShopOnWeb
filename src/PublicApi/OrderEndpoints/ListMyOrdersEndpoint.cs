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
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string PaymentStatus { get; set; } = "AwaitingPayment";
    public List<MyOrderItemDto> Items { get; set; } = new();
    public PaymentDto? Payment { get; set; }
}

public class MyOrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class ListMyOrdersResponse : BaseResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}

/// <summary>Lists the caller's orders with their payment state.</summary>
public class ListMyOrdersEndpoint : IEndpoint<IResult>
{
    private readonly IHttpContextAccessor _http;

    public ListMyOrdersEndpoint(IHttpContextAccessor http) => _http = http;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async () =>
                await HandleAsync())
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        var ctx = _http.HttpContext!;
        var buyerId = ctx.User.GetBuyerId();
        var paymentService = ctx.RequestServices.GetRequiredService<IPaymentService>();

        var orders = await paymentService.GetMyOrdersAsync(buyerId, ctx.RequestAborted);

        var response = new ListMyOrdersResponse();
        response.Orders = orders.Select(op => new MyOrderDto
        {
            OrderId = op.Order.Id,
            OrderDate = op.Order.OrderDate,
            Total = op.Order.Total(),
            PaymentStatus = op.Payment?.Status.ToString() ?? "AwaitingPayment",
            Items = op.Order.OrderItems.Select(i => new MyOrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            Payment = op.Payment?.ToDto()
        }).ToList();

        return Results.Ok(response);
    }
}
