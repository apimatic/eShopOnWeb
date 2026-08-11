using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Lists the caller's own orders together with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, HttpContext, IOrderQueryService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext context, IOrderQueryService orderQueryService) => await HandleAsync(context, orderQueryService))
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext context, IOrderQueryService queryService)
    {
        var orders = await queryService.GetOrdersForBuyerAsync(context.User.BuyerId());

        var response = new MyOrdersResponse
        {
            Orders = orders.Select(MapOrder).ToList()
        };

        return Results.Ok(response);
    }

    private static MyOrderDto MapOrder(OrderWithPayment entry)
    {
        var order = entry.Order;
        var payment = entry.Payment;

        return new MyOrderDto
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Currency = payment?.CurrencyCode ?? string.Empty,
            Status = (payment?.Status ?? PaymentStatus.AwaitingPayment).ToString(),
            Payment = payment is null ? null : MapPayment(payment),
            Items = order.OrderItems.Select(oi => new OrderLineResponse
            {
                CatalogItemId = oi.ItemOrdered.CatalogItemId,
                ProductName = oi.ItemOrdered.ProductName,
                UnitPrice = oi.UnitPrice,
                Units = oi.Units
            }).ToList()
        };
    }

    private static PaymentStateDto MapPayment(Payment payment) => new()
    {
        PayPalOrderId = payment.PayPalOrderId,
        AuthorizationId = payment.AuthorizationId,
        AuthorizationStatus = payment.AuthorizationStatus,
        AuthorizedAt = payment.AuthorizedAt,
        CaptureId = payment.CaptureId,
        CaptureStatus = payment.CaptureStatus,
        CapturedAmount = payment.CapturedAmount,
        PayPalFee = payment.PayPalFee,
        NetAmount = payment.NetAmount,
        CapturedAt = payment.CapturedAt,
        PaymentMethod = payment.PaymentMethodDescription,
        TotalRefunded = payment.TotalRefunded(),
        Refunds = payment.Refunds.Select(r => new RefundDto
        {
            RefundId = r.PayPalRefundId,
            Amount = r.Amount,
            Status = r.Status,
            CreatedAt = r.CreatedAt
        }).ToList()
    };
}

public class MyOrdersResponse : BaseResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public PaymentStateDto? Payment { get; set; }
    public List<OrderLineResponse> Items { get; set; } = new();
}

public class PaymentStateDto
{
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizedAt { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public DateTimeOffset? CapturedAt { get; set; }
    public string? PaymentMethod { get; set; }
    public decimal TotalRefunded { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();
}

public class RefundDto
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
