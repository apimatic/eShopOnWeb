using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, IRepository<Order>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user, IRepository<Order> orderRepo) =>
            {
                return await HandleAsync(new MyOrdersRequest { BuyerId = user.Identity?.Name ?? "" }, orderRepo);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(MyOrdersRequest request, IRepository<Order> orderRepo)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
            return Results.Unauthorized();

        var spec = new CustomerOrdersWithPaymentSpec(request.BuyerId);
        var orders = await orderRepo.ListAsync(spec);

        var response = new MyOrdersResponse(request.CorrelationId());
        response.Orders = orders.Select(o => new OrderSummaryDto
        {
            OrderId = o.Id,
            OrderDate = o.OrderDate,
            Status = o.Status.ToString(),
            Total = o.Total(),
            CapturedAmount = o.CapturedAmount,
            PayPalFee = o.PayPalFee,
            NetAmount = o.NetAmount,
            TotalRefunded = o.TotalRefunded,
            Items = o.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                Quantity = i.Units,
                UnitPrice = i.UnitPrice
            }).ToList()
        }).ToList();

        return Results.Ok(response);
    }
}

public class MyOrdersRequest : BaseRequest
{
    public string BuyerId { get; set; } = "";
}

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(Guid correlationId) : base(correlationId) { }
    public List<OrderSummaryDto> Orders { get; set; } = new();
}

public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string? Status { get; set; }
    public decimal Total { get; set; }
    public decimal CapturedAmount { get; set; }
    public decimal PayPalFee { get; set; }
    public decimal NetAmount { get; set; }
    public decimal TotalRefunded { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string? ProductName { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
