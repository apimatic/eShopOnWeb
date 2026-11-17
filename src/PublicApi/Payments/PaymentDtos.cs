using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class OrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class CreateOrderRequest : BaseRequest
{
    public List<OrderLineRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShipToAddress { get; set; }

    /// <summary>Optional caller key; when supplied, a repeated request returns the same order.</summary>
    public string? IdempotencyKey { get; set; }
}

public class CardAddressDetails
{
    public string? Line1 { get; set; }
    public string? Line2 { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
}

public class CardDetails
{
    public string Number { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public string Name { get; set; } = string.Empty;
    public CardAddressDetails? Address { get; set; }
}

public class PayOrderRequest : BaseRequest
{
    public CardDetails? Card { get; set; }
    public string? PaymentMethodId { get; set; }
}

public class CreatePaymentMethodRequest : BaseRequest
{
    public CardDetails Card { get; set; } = new();

    /// <summary>Optional caller key; when supplied, a repeated request returns the same card.</summary>
    public string? IdempotencyKey { get; set; }
}

public class RefundOrderRequest : BaseRequest
{
    /// <summary>Amount to refund in cents. Omit to refund everything still refundable.</summary>
    public long? AmountCents { get; set; }

    /// <summary>Caller-supplied key that makes a repeated refund request a no-op.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }
    public CreateOrderResponse() { }

    public int OrderId { get; set; }
    public long AmountCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }
    public PayOrderResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? AuthorizationState { get; set; }
    public long AmountCents { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public class FulfilOrderResponse : BaseResponse
{
    public FulfilOrderResponse(Guid correlationId) : base(correlationId) { }
    public FulfilOrderResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? AuthorizationState { get; set; }
    public string? CaptureTransferId { get; set; }
    public long? CapturedAmountCents { get; set; }
    public long? FeeCents { get; set; }
    public long? NetCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? Message { get; set; }
}

public class CancelOrderResponse : BaseResponse
{
    public CancelOrderResponse(Guid correlationId) : base(correlationId) { }
    public CancelOrderResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public bool IsVoid { get; set; }
    public string? VoidState { get; set; }
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    public int OrderId { get; set; }
    public string RefundId { get; set; } = string.Empty;
    public long AmountCents { get; set; }
    public long RefundedCents { get; set; }
    public long RemainingRefundableCents { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class PaymentMethodDto
{
    public string PaymentMethodId { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public CreatePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public CreatePaymentMethodResponse() { }

    public string PaymentMethodId { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public ListPaymentMethodsResponse(Guid correlationId) : base(correlationId) { }
    public ListPaymentMethodsResponse() { }

    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
    public bool Complete { get; set; } = true;
}

public class OrderItemSummaryDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
    public decimal LineTotal { get; set; }
}

public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public long AmountCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? AuthorizationState { get; set; }
    public bool IsVoid { get; set; }
    public string? VoidState { get; set; }
    public string? CaptureTransferId { get; set; }
    public long? CapturedAmountCents { get; set; }
    public long? FeeCents { get; set; }
    public long? NetCents { get; set; }
    public long RefundedCents { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public List<OrderItemSummaryDto> Items { get; set; } = new();
}

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(Guid correlationId) : base(correlationId) { }
    public MyOrdersResponse() { }

    public List<OrderSummaryDto> Orders { get; set; } = new();
}

public class ReconciliationEntryDto
{
    public string Kind { get; set; } = string.Empty;
    public string FinixId { get; set; } = string.Empty;
    public int? OrderId { get; set; }
    public long? AmountCents { get; set; }
    public string? State { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string MatchStatus { get; set; } = string.Empty;
    public string? OrderStatus { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public bool Complete { get; set; } = true;
    public List<ReconciliationEntryDto> Entries { get; set; } = new();
    public int MatchedCount { get; set; }
    public int FinixOnlyCount { get; set; }
    public int EshopOnlyCount { get; set; }
}
