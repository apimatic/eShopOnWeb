using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderResponse : BaseResponse
{
    public OrderResponse(Guid correlationId) : base(correlationId) { }
    public OrderResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public long TotalMinor { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public List<OrderItemResponse> Items { get; set; } = new();
    public PaymentResponse? Payment { get; set; }
    public string? Message { get; set; }

    public static OrderResponse From(OrderView view, Guid correlationId, string? message = null) =>
        new(correlationId)
        {
            OrderId = view.OrderId,
            Status = view.Status,
            Total = view.Total,
            TotalMinor = view.TotalMinor,
            Currency = view.Currency,
            OrderDate = view.OrderDate,
            Items = view.Items.Select(OrderItemResponse.From).ToList(),
            Payment = view.Payment is null ? null : PaymentResponse.From(view.Payment),
            Message = message
        };
}

public class OrderItemResponse
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }

    public static OrderItemResponse From(OrderItemView view) =>
        new()
        {
            CatalogItemId = view.CatalogItemId,
            ProductName = view.ProductName,
            UnitPrice = view.UnitPrice,
            Quantity = view.Quantity,
            LineTotal = view.LineTotal
        };
}

public class PaymentResponse
{
    public string Status { get; set; } = string.Empty;
    public long AmountMinor { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? AuthorizationState { get; set; }
    public string? VoidState { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureState { get; set; }
    public long? CapturedAmountMinor { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? FeeMinor { get; set; }
    public decimal? Fee { get; set; }
    public decimal? NetMinor { get; set; }
    public decimal? Net { get; set; }
    public long RefundedAmountMinor { get; set; }
    public decimal RefundedAmount { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public List<RefundResponse> Refunds { get; set; } = new();

    public static PaymentResponse From(PaymentView view) =>
        new()
        {
            Status = view.Status,
            AmountMinor = view.AmountMinor,
            Amount = view.Amount,
            Currency = view.Currency,
            AuthorizationId = view.AuthorizationId,
            AuthorizationState = view.AuthorizationState,
            VoidState = view.VoidState,
            CaptureId = view.CaptureId,
            CaptureState = view.CaptureState,
            CapturedAmountMinor = view.CapturedAmountMinor,
            CapturedAmount = view.CapturedAmount,
            FeeMinor = view.FeeMinor,
            Fee = view.Fee,
            NetMinor = view.NetMinor,
            Net = view.Net,
            RefundedAmountMinor = view.RefundedAmountMinor,
            RefundedAmount = view.RefundedAmount,
            FailureCode = view.FailureCode,
            FailureMessage = view.FailureMessage,
            Refunds = view.Refunds.Select(RefundResponse.From).ToList()
        };
}

public class RefundResponse
{
    public int RefundId { get; set; }
    public string? FinixRefundId { get; set; }
    public decimal Amount { get; set; }
    public long AmountMinor { get; set; }
    public string Status { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static RefundResponse From(RefundView view) =>
        new()
        {
            RefundId = view.RefundId,
            FinixRefundId = view.FinixRefundId,
            Amount = view.Amount,
            AmountMinor = view.AmountMinor,
            Status = view.Status,
            IdempotencyKey = view.IdempotencyKey,
            FailureCode = view.FailureCode,
            FailureMessage = view.FailureMessage,
            CreatedAt = view.CreatedAt
        };
}
