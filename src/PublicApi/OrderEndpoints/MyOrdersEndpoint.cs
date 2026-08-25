using System;
using System.Collections.Generic;
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

public class MyOrdersEndpoint : IEndpoint
{
    private readonly IReadRepository<Order> _orderRepo;

    public MyOrdersEndpoint(IReadRepository<Order> orderRepo)
    {
        _orderRepo = orderRepo;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext ctx) =>
            {
                var buyerId = ctx.User.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();
                return await HandleAsync(buyerId, ctx.RequestAborted);
            })
            .Produces<MyOrdersResponse>(200)
            .WithTags("OrderEndpoints");
    }

    private async Task<IResult> HandleAsync(string buyerId, System.Threading.CancellationToken ct)
    {
        var spec = new CustomerOrdersWithItemsSpecification(buyerId);
        var orders = await _orderRepo.ListAsync(spec, ct);

        var result = new List<OrderSummary>();
        foreach (var order in orders)
        {
            result.Add(new OrderSummary
            {
                OrderId = order.Id,
                OrderDate = order.OrderDate,
                Total = order.Total(),
                PaymentStatus = order.PaymentStatus.ToString(),
                CapturedAmount = order.CapturedAmount,
                TotalRefunded = order.TotalRefundedAmount > 0 ? order.TotalRefundedAmount : null
            });
        }

        return Results.Ok(new MyOrdersResponse { Orders = result });
    }
}

public class MyOrdersResponse
{
    public List<OrderSummary> Orders { get; set; } = new();
}

public class OrderSummary
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal? CapturedAmount { get; set; }
    public decimal? TotalRefunded { get; set; }
}
