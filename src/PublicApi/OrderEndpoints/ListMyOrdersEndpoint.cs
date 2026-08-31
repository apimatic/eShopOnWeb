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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Lists the caller's orders with their payment state.
/// </summary>
public class ListMyOrdersEndpoint : IEndpoint<IResult, ListMyOrdersRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, IOrderPaymentService orderPaymentService) =>
            {
                var request = new ListMyOrdersRequest { BuyerId = httpContext.User.Identity?.Name };
                return await HandleAsync(request, orderPaymentService);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMyOrdersRequest request, IOrderPaymentService orderPaymentService)
    {
        var response = new ListMyOrdersResponse(request.CorrelationId());

        var orders = await orderPaymentService.GetOrdersForBuyerAsync(request.BuyerId!);
        foreach (var order in orders)
        {
            var payment = await orderPaymentService.GetActivePaymentForOrderAsync(order.Id);
            response.Orders.Add(new MyOrderDto
            {
                OrderId = order.Id,
                OrderDate = order.OrderDate,
                Status = order.Status.ToString(),
                Total = order.Total(),
                Items = order.OrderItems.Select(i => new OrderLineDto
                {
                    CatalogItemId = i.ItemOrdered.CatalogItemId,
                    ProductName = i.ItemOrdered.ProductName,
                    UnitPrice = i.UnitPrice,
                    Units = i.Units
                }).ToList(),
                Payment = payment is null ? null : ToDto(payment)
            });
        }

        return Results.Ok(response);
    }

    internal static PaymentDto ToDto(Payment payment)
    {
        return new PaymentDto
        {
            PaymentId = payment.Id,
            Status = payment.Status,
            AuthorizationId = payment.AuthorizationId,
            AuthorizationStatus = payment.AuthorizationStatus,
            CaptureId = payment.CaptureId,
            CaptureStatus = payment.CaptureStatus,
            AuthorizedAmount = payment.AuthorizedAmount,
            CapturedAmount = payment.CapturedAmount,
            PayPalFee = payment.PayPalFee,
            NetAmount = payment.NetAmount,
            Currency = payment.Currency,
            CardBrand = payment.CardBrand,
            CardLast4 = payment.CardLast4,
            SavedPaymentMethodId = payment.SavedPaymentMethodId,
            TotalRefunded = payment.TotalRefunded,
            RemainingRefundable = payment.RemainingRefundable,
            Refunds = payment.Refunds.Select(r => new RefundDto
            {
                RefundId = r.Id,
                PayPalRefundId = r.PayPalRefundId,
                Amount = r.Amount,
                Status = r.Status,
                IdempotencyKey = r.IdempotencyKey,
                CreatedAt = r.CreatedAt
            }).ToList()
        };
    }
}

public class ListMyOrdersRequest : BaseRequest
{
    public string? BuyerId { get; set; }
}

public class ListMyOrdersResponse : BaseResponse
{
    public ListMyOrdersResponse(Guid correlationId) : base(correlationId) { }
    public ListMyOrdersResponse() { }

    public List<MyOrderDto> Orders { get; set; } = new List<MyOrderDto>();
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<OrderLineDto> Items { get; set; } = new List<OrderLineDto>();
    public PaymentDto? Payment { get; set; }
}

public class PaymentDto
{
    public int PaymentId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal AuthorizedAmount { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? CardBrand { get; set; }
    public string? CardLast4 { get; set; }
    public int? SavedPaymentMethodId { get; set; }
    public decimal TotalRefunded { get; set; }
    public decimal RemainingRefundable { get; set; }
    public List<RefundDto> Refunds { get; set; } = new List<RefundDto>();
}

public class RefundDto
{
    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
