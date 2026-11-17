using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class ShippingAddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public ShippingAddressDto? ShippingAddress { get; set; }
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
    public decimal LineTotal { get; set; }
}

public class RefundDto
{
    public string RefundId { get; set; } = string.Empty;
    public int PaymentRefundId { get; set; }
    public long AmountCents { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? State { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class OrderDto
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public long TotalCents { get; set; }
    public string? Currency { get; set; }
    public string? FinixAuthorizationId { get; set; }
    public string? FinixAuthorizationState { get; set; }
    public long? AuthorizedAmountCents { get; set; }
    public string? FinixPaymentInstrumentId { get; set; }
    public string? FinixCaptureTransferId { get; set; }
    public string? FinixCaptureState { get; set; }
    public long? CapturedAmountCents { get; set; }
    public decimal? CapturedAmount { get; set; }
    public long? FinixFeeCents { get; set; }
    public decimal? FinixFee { get; set; }
    public long? NetProceedsCents { get; set; }
    public decimal? NetProceeds { get; set; }
    public long RefundedAmountCents { get; set; }
    public decimal RefundedAmount { get; set; }
    public long RefundableAmountCents { get; set; }
    public decimal RefundableAmount { get; set; }
    public DateTimeOffset? FulfilledAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public List<RefundDto> Refunds { get; set; } = new();
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse()
    {
    }

    public CreateOrderResponse(Guid correlationId) : base(correlationId)
    {
    }

    public int OrderId { get; set; }
    public OrderDto Order { get; set; } = new();
}

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse()
    {
    }

    public MyOrdersResponse(Guid correlationId) : base(correlationId)
    {
    }

    public List<OrderDto> Orders { get; set; } = new();
}

public class CardBillingAddressRequest
{
    public string? Line1 { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
}

public class CardPaymentRequest
{
    public string Number { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public string Name { get; set; } = string.Empty;
    public CardBillingAddressRequest? BillingAddress { get; set; }
}

public class PayOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public int? PaymentMethodId { get; set; }
    public int? SavedPaymentMethodId { get; set; }
    public CardPaymentRequest? Card { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse()
    {
    }

    public PayOrderResponse(Guid correlationId) : base(correlationId)
    {
    }

    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? AuthorizationState { get; set; }
    public long? AuthorizedAmountCents { get; set; }
    public string? Currency { get; set; }
    public string? PaymentInstrumentId { get; set; }
    public OrderDto Order { get; set; } = new();
}

public class FulfilOrderResponse : BaseResponse
{
    public FulfilOrderResponse()
    {
    }

    public FulfilOrderResponse(Guid correlationId) : base(correlationId)
    {
    }

    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? CaptureTransferId { get; set; }
    public string? CaptureState { get; set; }
    public long? CapturedAmountCents { get; set; }
    public long? FeeCents { get; set; }
    public long? NetProceedsCents { get; set; }
    public string? Currency { get; set; }
    public OrderDto Order { get; set; } = new();
}

public class CancelOrderResponse : BaseResponse
{
    public CancelOrderResponse()
    {
    }

    public CancelOrderResponse(Guid correlationId) : base(correlationId)
    {
    }

    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? AuthorizationState { get; set; }
    public OrderDto Order { get; set; } = new();
}

public class RefundOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public decimal? Amount { get; set; }
    public long? AmountCents { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? Reason { get; set; }
}

public class RefundResponse : BaseResponse
{
    public RefundResponse()
    {
    }

    public RefundResponse(Guid correlationId) : base(correlationId)
    {
    }

    public string RefundId { get; set; } = string.Empty;
    public int PaymentRefundId { get; set; }
    public int OrderId { get; set; }
    public long AmountCents { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? State { get; set; }
    public long RefundedAmountCents { get; set; }
    public long RefundableAmountCents { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
}

public class SavePaymentMethodRequest : BaseRequest
{
    public CardPaymentRequest Card { get; set; } = new();
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public string? CardholderName { get; set; }
    public string? FinixPaymentInstrumentId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class SavePaymentMethodResponse : BaseResponse
{
    public SavePaymentMethodResponse()
    {
    }

    public SavePaymentMethodResponse(Guid correlationId) : base(correlationId)
    {
    }

    public int PaymentMethodId { get; set; }
    public PaymentMethodDto PaymentMethod { get; set; } = new();
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public ListPaymentMethodsResponse()
    {
    }

    public ListPaymentMethodsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

public class ReconciliationEntryDto
{
    public string Source { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string? Id { get; set; }
    public string? ParentId { get; set; }
    public string? Type { get; set; }
    public string? State { get; set; }
    public long? AmountCents { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public int? OrderId { get; set; }
    public string? RefundId { get; set; }
    public string MatchStatus { get; set; } = string.Empty;
    public string? Detail { get; set; }
}

public class ReconciliationSummary
{
    public int FinixTransactionCount { get; set; }
    public int MatchedCount { get; set; }
    public int FinixOnlyCount { get; set; }
    public int EShopOnlyCount { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse()
    {
    }

    public ReconciliationResponse(Guid correlationId) : base(correlationId)
    {
    }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationEntryDto> Entries { get; set; } = new();
    public List<ReconciliationEntryDto> Unmatched { get; set; } = new();
    public ReconciliationSummary Summary { get; set; } = new();
}

public class FulfilOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public class CancelOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public class DeletePaymentMethodRequest : BaseRequest
{
    public int PaymentMethodId { get; set; }
}

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}
