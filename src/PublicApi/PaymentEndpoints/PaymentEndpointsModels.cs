using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();

    public List<CreateOrderItemRequest>? Lines { get; set; }

    public List<CreateOrderItemRequest>? CatalogItems { get; set; }

    public ShippingAddressRequest? ShipToAddress { get; set; }

    public ShippingAddressRequest? ShippingAddress { get; set; }

    [JsonIgnore]
    public IReadOnlyList<CreateOrderItemRequest> RequestedItems =>
        Items.Count > 0 ? Items : Lines ?? CatalogItems ?? new List<CreateOrderItemRequest>();

    [JsonIgnore]
    public ShippingAddressRequest? RequestedAddress => ShipToAddress ?? ShippingAddress;
}

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }

    /// <summary>Alias for CatalogItemId.</summary>
    public int Id { get; set; }

    public int Quantity { get; set; }

    [JsonIgnore]
    public int RequestedCatalogItemId => CatalogItemId != 0 ? CatalogItemId : Id;
}

public class ShippingAddressRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class CardRequest
{
    public string Number { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public string Name { get; set; } = string.Empty;
    public ShippingAddressRequest? Address { get; set; }

    /// <summary>Alias for Number.</summary>
    public string? CardNumber { get; set; }

    /// <summary>Alias for ExpirationMonth.</summary>
    public int? ExpiryMonth { get; set; }

    /// <summary>Alias for ExpirationMonth.</summary>
    public int? ExpMonth { get; set; }

    /// <summary>Alias for ExpirationYear.</summary>
    public int? ExpiryYear { get; set; }

    /// <summary>Alias for ExpirationYear.</summary>
    public int? ExpYear { get; set; }

    /// <summary>Alias for SecurityCode.</summary>
    public string? Cvv { get; set; }

    /// <summary>Alias for SecurityCode.</summary>
    public string? Cvc { get; set; }

    /// <summary>Alias for Name.</summary>
    public string? CardholderName { get; set; }

    [JsonIgnore]
    public string EffectiveNumber => !string.IsNullOrWhiteSpace(Number) ? Number : CardNumber ?? string.Empty;

    [JsonIgnore]
    public int EffectiveExpirationMonth => ExpirationMonth != 0 ? ExpirationMonth : ExpiryMonth ?? ExpMonth ?? 0;

    [JsonIgnore]
    public int EffectiveExpirationYear => ExpirationYear != 0 ? ExpirationYear : ExpiryYear ?? ExpYear ?? 0;

    [JsonIgnore]
    public string? EffectiveSecurityCode => !string.IsNullOrWhiteSpace(SecurityCode) ? SecurityCode : Cvv ?? Cvc;

    [JsonIgnore]
    public string EffectiveName => !string.IsNullOrWhiteSpace(Name) ? Name : CardholderName ?? string.Empty;
}

public class PayOrderRequest : BaseRequest
{
    [JsonIgnore]
    public int OrderId { get; set; }

    public CardRequest? Card { get; set; }
    public int? PaymentMethodId { get; set; }

    /// <summary>Alias for PaymentMethodId.</summary>
    public int? SavedPaymentMethodId { get; set; }

    [JsonIgnore]
    public int? RequestedPaymentMethodId => PaymentMethodId ?? SavedPaymentMethodId;
}

public class RefundOrderRequest : BaseRequest
{
    [JsonIgnore]
    public int OrderId { get; set; }

    /// <summary>Amount to refund in major units (for example 12.34). Omit to refund the remaining captured amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Amount to refund in cents. Takes precedence over Amount when supplied.</summary>
    public long? AmountCents { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;
}

public class SavePaymentMethodRequest : BaseRequest
{
    public CardRequest Card { get; set; } = new();
}

public class DeletePaymentMethodRequest
{
    public int PaymentMethodId { get; set; }
}

public class ReconciliationRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
}

public class PaymentDto
{
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal AuthorizedAmount { get; set; }
    public decimal CapturedAmount { get; set; }
    public decimal Fee { get; set; }
    public decimal Net { get; set; }
    public decimal RefundedAmount { get; set; }
    public decimal RefundableAmount { get; set; }
    public string? FinixAuthorizationId { get; set; }
    public string? FinixAuthorizationState { get; set; }
    public string? FinixTransferId { get; set; }
    public string? FinixTransferState { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();
}

public class RefundDto
{
    public int RefundId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? FinixRefundTransferId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class OrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public ShippingAddressRequest? ShipToAddress { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public PaymentDto? Payment { get; set; }
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? CardholderName { get; set; }
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }
    public CreateOrderResponse() { }

    public int OrderId { get; set; }
    public OrderDto Order { get; set; } = new();
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }
    public PayOrderResponse() { }

    public int OrderId { get; set; }
    public PaymentDto Payment { get; set; } = new();
}

public class FulfilOrderResponse : BaseResponse
{
    public FulfilOrderResponse(Guid correlationId) : base(correlationId) { }
    public FulfilOrderResponse() { }

    public int OrderId { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public PaymentDto Payment { get; set; } = new();
}

public class CancelOrderResponse : BaseResponse
{
    public CancelOrderResponse(Guid correlationId) : base(correlationId) { }
    public CancelOrderResponse() { }

    public int OrderId { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public PaymentDto Payment { get; set; } = new();
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    public int RefundId { get; set; }
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? FinixRefundTransferId { get; set; }
}

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(Guid correlationId) : base(correlationId) { }
    public MyOrdersResponse() { }

    public List<OrderDto> Orders { get; set; } = new();
}

public class SavePaymentMethodResponse : BaseResponse
{
    public SavePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public SavePaymentMethodResponse() { }

    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? CardholderName { get; set; }
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public ListPaymentMethodsResponse(Guid correlationId) : base(correlationId) { }
    public ListPaymentMethodsResponse() { }

    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

public class DeletePaymentMethodResponse : BaseResponse
{
    public DeletePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public DeletePaymentMethodResponse() { }

    public int PaymentMethodId { get; set; }
    public bool Deleted { get; set; }
}

public class ReconciliationFinixTransactionDto
{
    public string FinixId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public long AmountCents { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset? CreatedAt { get; set; }
    public int? MatchedOrderId { get; set; }
    public bool Matched { get; set; }
}

public class ReconciliationOrderDto
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public long AmountCents { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public string? FinixAuthorizationId { get; set; }
    public string? FinixTransferId { get; set; }
    public List<string> FinixRefundIds { get; set; } = new();
    public bool Matched { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
    public int FinixAuthorizationCount { get; set; }
    public int FinixTransferCount { get; set; }
    public int OrderCount { get; set; }
    public List<ReconciliationFinixTransactionDto> FinixTransactions { get; set; } = new();
    public List<ReconciliationOrderDto> Orders { get; set; } = new();
    public List<ReconciliationFinixTransactionDto> UnmatchedFinixTransactions { get; set; } = new();
    public List<ReconciliationOrderDto> UnmatchedOrders { get; set; } = new();
}
