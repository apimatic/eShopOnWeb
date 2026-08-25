using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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

public class MyOrdersEndpoint : IEndpoint<IResult, string, IReadRepository<Order>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IReadRepository<Order> orderRepo, HttpContext ctx, CancellationToken ct) =>
            {
                var buyerId = ctx.User.Identity?.Name ?? string.Empty;
                return await HandleAsync(buyerId, orderRepo);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(string buyerId, IReadRepository<Order> orderRepo)
    {
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var spec = new CustomerOrdersWithItemsSpecification(buyerId);
        var orders = await orderRepo.ListAsync(spec);

        var dtos = orders.Select(o => new OrderDto
        {
            OrderId = o.Id,
            OrderDate = o.OrderDate,
            Total = o.Total(),
            PaymentStatus = o.PaymentStatus.ToString(),
            AuthorizationId = o.AuthorizationId,
            CaptureId = o.CaptureId,
            CapturedAmount = o.CapturedAmount,
            PayPalFee = o.PayPalFee,
            NetAmount = o.NetAmount,
            Refunds = o.Refunds.Select(r => new RefundDto
            {
                RefundId = r.RefundId,
                Amount = r.Amount,
                RefundedAt = r.RefundedAt
            }).ToList()
        }).ToList();

        return Results.Ok(new MyOrdersResponse { Orders = dtos });
    }
}

public class MyOrdersResponse
{
    public List<OrderDto> Orders { get; set; } = new();
}

public class OrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();
}

public class RefundDto
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTimeOffset RefundedAt { get; set; }
}
