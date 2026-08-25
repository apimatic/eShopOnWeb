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
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrderDto
{
    public int OrderId { get; set; }
    public System.DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? PayPalAuthorizationId { get; set; }
    public string? PayPalCaptureId { get; set; }
    public List<MyOrderItemDto> Items { get; set; } = new();
}

public class MyOrderItemDto
{
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public class MyOrdersEndpoint : IEndpoint<IResult, EmptyRequest, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext ctx) =>
            {
                return await HandleAsync(new EmptyRequest(), ctx);
            })
            .Produces<List<MyOrderDto>>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(EmptyRequest request, HttpContext ctx)
    {
        var buyerId = ctx.User.FindFirstValue(ClaimTypes.Name)!;
        var sp = ctx.RequestServices;
        var orderRepo = sp.GetRequiredService<IReadRepository<Order>>();
        var paymentRepo = sp.GetRequiredService<IReadRepository<Payment>>();
        var ct = ctx.RequestAborted;

        var ordersSpec = new CustomerOrdersWithItemsSpecification(buyerId);
        var orders = await orderRepo.ListAsync(ordersSpec, ct);

        var result = new List<MyOrderDto>();
        foreach (var order in orders)
        {
            var paymentSpec = new PaymentByOrderIdSpec(order.Id);
            var payment = await paymentRepo.FirstOrDefaultAsync(paymentSpec, ct);

            result.Add(new MyOrderDto
            {
                OrderId = order.Id,
                OrderDate = order.OrderDate,
                Total = order.Total(),
                PaymentStatus = payment?.Status.ToString() ?? "Unknown",
                PayPalAuthorizationId = payment?.PayPalAuthorizationId,
                PayPalCaptureId = payment?.PayPalCaptureId,
                Items = order.OrderItems.Select(i => new MyOrderItemDto
                {
                    ProductName = i.ItemOrdered.ProductName,
                    Quantity = i.Units,
                    UnitPrice = i.UnitPrice
                }).ToList()
            });
        }

        return Results.Ok(result);
    }
}
