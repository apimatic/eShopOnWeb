using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }

    public static OrderLineDto From(OrderLineResult result) => new()
    {
        CatalogItemId = result.CatalogItemId,
        ProductName = result.ProductName,
        UnitPrice = result.UnitPrice,
        Quantity = result.Quantity,
        LineTotal = result.LineTotal
    };
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }
    public CreateOrderResponse() { }

    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public long TotalCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public List<OrderLineDto> Items { get; set; } = new();

    public static CreateOrderResponse From(CreatedOrderResult result, Guid correlationId) => new(correlationId)
    {
        OrderId = result.OrderId,
        PaymentStatus = result.PaymentStatus,
        Status = result.PaymentStatus,
        Total = result.Total,
        TotalCents = result.TotalCents,
        Currency = result.Currency,
        OrderDate = result.OrderDate,
        Items = result.Items.Select(OrderLineDto.From).ToList()
    };
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }
    public PayOrderResponse() { }

    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? AuthorizationState { get; set; }
    public string? PaymentInstrumentId { get; set; }
    public decimal Amount { get; set; }
    public long AmountCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? Message { get; set; }

    public static PayOrderResponse From(PaymentActionResult result, Guid correlationId) => new(correlationId)
    {
        OrderId = result.OrderId,
        PaymentStatus = result.PaymentStatus,
        Status = result.PaymentStatus,
        AuthorizationId = result.AuthorizationId,
        AuthorizationState = result.AuthorizationState,
        PaymentInstrumentId = result.PaymentInstrumentId,
        Amount = result.Amount,
        AmountCents = result.AmountCents,
        Currency = result.Currency,
        Message = result.Message
    };
}

public class FulfilOrderResponse : BaseResponse
{
    public FulfilOrderResponse(Guid correlationId) : base(correlationId) { }
    public FulfilOrderResponse() { }

    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? CaptureTransferId { get; set; }
    public string? CaptureState { get; set; }
    public decimal CapturedAmount { get; set; }
    public long CapturedAmountCents { get; set; }
    public decimal FinixFee { get; set; }
    public long FinixFeeCents { get; set; }
    public decimal NetProceeds { get; set; }
    public long NetProceedsCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? Message { get; set; }

    public static FulfilOrderResponse From(FulfilmentResult result, Guid correlationId) => new(correlationId)
    {
        OrderId = result.OrderId,
        PaymentStatus = result.PaymentStatus,
        Status = result.PaymentStatus,
        AuthorizationId = result.AuthorizationId,
        CaptureTransferId = result.CaptureTransferId,
        CaptureState = result.CaptureState,
        CapturedAmount = result.CapturedAmount,
        CapturedAmountCents = result.CapturedAmountCents,
        FinixFee = result.FinixFee,
        FinixFeeCents = result.FinixFeeCents,
        NetProceeds = result.NetProceeds,
        NetProceedsCents = result.NetProceedsCents,
        Currency = result.Currency,
        Message = result.Message
    };
}

public class CancelOrderResponse : BaseResponse
{
    public CancelOrderResponse(Guid correlationId) : base(correlationId) { }
    public CancelOrderResponse() { }

    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? VoidState { get; set; }
    public string? Message { get; set; }

    public static CancelOrderResponse From(CancelResult result, Guid correlationId) => new(correlationId)
    {
        OrderId = result.OrderId,
        PaymentStatus = result.PaymentStatus,
        Status = result.PaymentStatus,
        AuthorizationId = result.AuthorizationId,
        VoidState = result.VoidState,
        Message = result.Message
    };
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    public int RefundId { get; set; }
    public int OrderId { get; set; }
    public string? FinixRefundTransferId { get; set; }
    public decimal RefundedAmount { get; set; }
    public long RefundedAmountCents { get; set; }
    public decimal TotalRefunded { get; set; }
    public long TotalRefundedCents { get; set; }
    public decimal RemainingRefundable { get; set; }
    public long RemainingRefundableCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Message { get; set; }

    public static RefundOrderResponse From(RefundResult result, Guid correlationId) => new(correlationId)
    {
        RefundId = result.RefundId,
        OrderId = result.OrderId,
        FinixRefundTransferId = result.FinixRefundTransferId,
        RefundedAmount = result.RefundedAmount,
        RefundedAmountCents = result.RefundedAmountCents,
        TotalRefunded = result.TotalRefunded,
        TotalRefundedCents = result.TotalRefundedCents,
        RemainingRefundable = result.RemainingRefundable,
        RemainingRefundableCents = result.RemainingRefundableCents,
        Currency = result.Currency,
        State = result.State,
        IdempotencyKey = result.IdempotencyKey,
        PaymentStatus = result.PaymentStatus,
        Status = result.PaymentStatus,
        Message = result.Message
    };
}

public class OrderRefundDto
{
    public int RefundId { get; set; }
    public string? FinixRefundTransferId { get; set; }
    public decimal Amount { get; set; }
    public long AmountCents { get; set; }
    public string State { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public long TotalCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? AuthorizationState { get; set; }
    public string? CaptureTransferId { get; set; }
    public string? CaptureState { get; set; }
    public decimal CapturedAmount { get; set; }
    public long CapturedAmountCents { get; set; }
    public decimal FinixFee { get; set; }
    public long FinixFeeCents { get; set; }
    public decimal NetProceeds { get; set; }
    public long NetProceedsCents { get; set; }
    public decimal RefundedAmount { get; set; }
    public long RefundedAmountCents { get; set; }
    public decimal RemainingRefundable { get; set; }
    public long RemainingRefundableCents { get; set; }
    public string? PaymentFailureMessage { get; set; }
    public List<OrderLineDto> Items { get; set; } = new();
    public List<OrderRefundDto> Refunds { get; set; } = new();

    public static OrderSummaryDto From(OrderSummaryResult result) => new()
    {
        OrderId = result.OrderId,
        OrderDate = result.OrderDate,
        Total = result.Total,
        TotalCents = result.TotalCents,
        Currency = result.Currency,
        PaymentStatus = result.PaymentStatus,
        Status = result.PaymentStatus,
        AuthorizationId = result.AuthorizationId,
        AuthorizationState = result.AuthorizationState,
        CaptureTransferId = result.CaptureTransferId,
        CaptureState = result.CaptureState,
        CapturedAmount = result.CapturedAmount,
        CapturedAmountCents = result.CapturedAmountCents,
        FinixFee = result.FinixFee,
        FinixFeeCents = result.FinixFeeCents,
        NetProceeds = result.NetProceeds,
        NetProceedsCents = result.NetProceedsCents,
        RefundedAmount = result.RefundedAmount,
        RefundedAmountCents = result.RefundedAmountCents,
        RemainingRefundable = result.RemainingRefundable,
        RemainingRefundableCents = result.RemainingRefundableCents,
        PaymentFailureMessage = result.PaymentFailureMessage,
        Items = result.Items.Select(OrderLineDto.From).ToList(),
        Refunds = result.Refunds.Select(r => new OrderRefundDto
        {
            RefundId = r.RefundId,
            FinixRefundTransferId = r.FinixRefundTransferId,
            Amount = r.Amount,
            AmountCents = r.AmountCents,
            State = r.State,
            IdempotencyKey = r.IdempotencyKey,
            CreatedAt = r.CreatedAt
        }).ToList()
    };
}

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(Guid correlationId) : base(correlationId) { }
    public MyOrdersResponse() { }

    public List<OrderSummaryDto> Orders { get; set; } = new();

    public static MyOrdersResponse From(IReadOnlyList<OrderSummaryResult> results, Guid correlationId) =>
        new(correlationId) { Orders = results.Select(OrderSummaryDto.From).ToList() };
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string PaymentInstrumentId { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? LastFour { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public string? Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static PaymentMethodDto From(SavedPaymentMethodResult result) => new()
    {
        PaymentMethodId = result.PaymentMethodId,
        PaymentInstrumentId = result.PaymentInstrumentId,
        Brand = result.Brand,
        Last4 = result.Last4,
        LastFour = result.Last4,
        ExpirationMonth = result.ExpirationMonth,
        ExpirationYear = result.ExpirationYear,
        Name = result.Name,
        CreatedAt = result.CreatedAt
    };
}

public class SavePaymentMethodResponse : BaseResponse
{
    public SavePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public SavePaymentMethodResponse() { }

    public int PaymentMethodId { get; set; }
    public string PaymentInstrumentId { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? LastFour { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public string? Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static SavePaymentMethodResponse From(SavedPaymentMethodResult result, Guid correlationId) => new(correlationId)
    {
        PaymentMethodId = result.PaymentMethodId,
        PaymentInstrumentId = result.PaymentInstrumentId,
        Brand = result.Brand,
        Last4 = result.Last4,
        LastFour = result.Last4,
        ExpirationMonth = result.ExpirationMonth,
        ExpirationYear = result.ExpirationYear,
        Name = result.Name,
        CreatedAt = result.CreatedAt
    };
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public ListPaymentMethodsResponse(Guid correlationId) : base(correlationId) { }
    public ListPaymentMethodsResponse() { }

    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();

    public static ListPaymentMethodsResponse From(
        IReadOnlyList<SavedPaymentMethodResult> results,
        Guid correlationId) =>
        new(correlationId) { PaymentMethods = results.Select(PaymentMethodDto.From).ToList() };
}

public class DeletePaymentMethodResponse : BaseResponse
{
    public DeletePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public DeletePaymentMethodResponse() { }

    public string PaymentMethodId { get; set; } = string.Empty;
    public bool Deleted { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Message { get; set; }
}

