using System.Collections.Generic;
using System.Linq;
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

public class MyOrderSummary
{
    public int OrderId { get; set; }
    public string OrderDate { get; set; } = "";
    public decimal Total { get; set; }
    public string PaymentStatus { get; set; } = "";
    public string? PayPalAuthorizationId { get; set; }
    public string? PayPalCaptureId { get; set; }
    public decimal CapturedAmount { get; set; }
    public decimal RefundedAmount { get; set; }
    public List<OrderItemSummary> Items { get; set; } = new();
}

public class OrderItemSummary
{
    public string ProductName { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public class MyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext ctx, IRepository<Order> orderRepo) =>
            {
                var buyerId = ctx.User.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                var spec = new CustomerOrdersWithPaymentSpec(buyerId);
                var orders = await orderRepo.ListAsync(spec);

                var result = orders.Select(o => new MyOrderSummary
                {
                    OrderId = o.Id,
                    OrderDate = o.OrderDate.ToString("O"),
                    Total = o.Total(),
                    PaymentStatus = o.PaymentStatus.ToString(),
                    PayPalAuthorizationId = o.PayPalAuthorizationId,
                    PayPalCaptureId = o.PayPalCaptureId,
                    CapturedAmount = o.CapturedAmount,
                    RefundedAmount = o.RefundedAmount,
                    Items = o.OrderItems.Select(i => new OrderItemSummary
                    {
                        ProductName = i.ItemOrdered.ProductName,
                        Quantity = i.Units,
                        UnitPrice = i.UnitPrice
                    }).ToList()
                }).ToList();

                return Results.Ok(result);
            })
            .Produces<List<MyOrderSummary>>()
            .WithTags("OrderEndpoints");
    }
}
