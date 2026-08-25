using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrdersResponse
{
    public List<OrderSummary> Orders { get; set; } = new();
}

public class OrderSummary
{
    public int OrderId { get; set; }
    public System.DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string PaymentStatus { get; set; } = "";
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public string Currency { get; set; } = "";
    public List<OrderItemSummary> Items { get; set; } = new();
    public List<RefundSummary> Refunds { get; set; } = new();
}

public class OrderItemSummary
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

public class RefundSummary
{
    public string RefundId { get; set; } = "";
    public decimal Amount { get; set; }
    public System.DateTimeOffset CreatedAt { get; set; }
}

public class MyOrdersEndpoint : IEndpoint<IResult, object, IRepository<Order>>
{
    private readonly IRepository<OrderPayment> _paymentRepo;

    public MyOrdersEndpoint(IRepository<OrderPayment> paymentRepo)
    {
        _paymentRepo = paymentRepo;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IRepository<Order> orderRepo, HttpContext ctx, CancellationToken ct) =>
            {
                var buyerId = ctx.User.FindFirst(ClaimTypes.Name)?.Value;
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                return await FetchOrdersAsync(orderRepo, buyerId, ct);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(object request, IRepository<Order> repository)
        => Task.FromResult(Results.Ok() as IResult);

    private async Task<IResult> FetchOrdersAsync(IRepository<Order> orderRepo,
        string buyerId, CancellationToken ct)
    {
        var orderSpec = new OrdersByBuyerSpec(buyerId);
        var orders = await orderRepo.ListAsync(orderSpec, ct);

        var paymentSpec = new AllOrderPaymentsSpec();
        var allPayments = await _paymentRepo.ListAsync(paymentSpec, ct);
        var paymentMap = allPayments
            .Where(p => p.BuyerId == buyerId)
            .ToDictionary(p => p.OrderId, p => p);

        var summaries = orders.Select(o =>
        {
            paymentMap.TryGetValue(o.Id, out var payment);
            return new OrderSummary
            {
                OrderId = o.Id,
                OrderDate = o.OrderDate,
                Total = o.Total(),
                PaymentStatus = payment?.Status.ToString() ?? "Unknown",
                AuthorizationId = payment?.AuthorizationId,
                CaptureId = payment?.CaptureId,
                CapturedAmount = payment?.CapturedAmount,
                PayPalFee = payment?.PayPalFee,
                NetAmount = payment?.NetAmount,
                Currency = payment?.Currency ?? "",
                Items = o.OrderItems.Select(i => new OrderItemSummary
                {
                    CatalogItemId = i.ItemOrdered.CatalogItemId,
                    ProductName = i.ItemOrdered.ProductName,
                    UnitPrice = i.UnitPrice,
                    Quantity = i.Units
                }).ToList(),
                Refunds = (payment?.Refunds ?? Enumerable.Empty<PaymentRefund>())
                    .Select(r => new RefundSummary
                    {
                        RefundId = r.PayPalRefundId,
                        Amount = r.Amount,
                        CreatedAt = r.CreatedAt
                    }).ToList()
            };
        }).ToList();

        return Results.Ok(new MyOrdersResponse { Orders = summaries });
    }
}
