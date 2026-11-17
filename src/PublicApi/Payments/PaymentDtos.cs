using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class CreateOrderItemRequest
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

public class CreateOrderRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShippingAddress { get; set; }
}

public class OrderItemResponse
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
    public decimal LineTotal { get; set; }
}

public class RefundResponse
{
    public int OrderId { get; set; }
    public string RefundId { get; set; } = string.Empty;
    public long AmountCents { get; set; }
    public decimal Amount { get; set; }
    public string State { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public bool IdempotentReplay { get; set; }
    public long TotalRefundedCents { get; set; }
    public long RemainingRefundableCents { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
}

public class OrderResponse
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public long TotalCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string FulfilmentStatus { get; set; } = string.Empty;

    public string? AuthorizationId { get; set; }
    public string? AuthorizationState { get; set; }
    public string? AuthorizationVoidState { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public bool AuthorizationVoided { get; set; }
    public long? AuthorizedAmountCents { get; set; }

    public string? CaptureId { get; set; }
    public string? CaptureState { get; set; }
    public long? CapturedAmountCents { get; set; }
    public long? FeeCents { get; set; }
    public long? NetProceedsCents { get; set; }

    public long RefundedAmountCents { get; set; }
    public long RemainingRefundableCents { get; set; }
    public string? PaymentFailureMessage { get; set; }

    public ShippingAddressRequest? ShipToAddress { get; set; }
    public List<OrderItemResponse> Items { get; set; } = new();
    public List<RefundResponse> Refunds { get; set; } = new();
}

public class MyOrdersResponse
{
    public List<OrderResponse> Orders { get; set; } = new();
}

public class CardAddressRequest
{
    public string? Line1 { get; set; }
    public string? Line2 { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
}

public class CardDetailsRequest
{
    public string? Number { get; set; }
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public CardAddressRequest? Address { get; set; }
}

public class PayOrderRequest
{
    /// <summary>A saved payment method owned by the caller. Mutually exclusive with Card.</summary>
    public int? PaymentMethodId { get; set; }

    /// <summary>One-off card details. Mutually exclusive with PaymentMethodId.</summary>
    public CardDetailsRequest? Card { get; set; }
}

public class SavePaymentMethodRequest
{
    public CardDetailsRequest? Card { get; set; }
}

public class SavedPaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class SavedPaymentMethodsResponse
{
    public List<SavedPaymentMethodResponse> PaymentMethods { get; set; } = new();
}

public class CreateRefundRequest
{
    /// <summary>Refund amount in cents. When omitted (and Amount is omitted) the remaining captured amount is refunded.</summary>
    public long? AmountCents { get; set; }

    /// <summary>Refund amount in the order currency (for example 5.00). Alternative to AmountCents.</summary>
    public decimal? Amount { get; set; }

    public string? IdempotencyKey { get; set; }
}

public class ReconciliationFinixTransaction
{
    public string ResourceType { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public DateTimeOffset? CreatedAt { get; set; }
    public long AmountCents { get; set; }
    public string? Currency { get; set; }
    public string? State { get; set; }
    public string? Type { get; set; }
    public string? OperationKey { get; set; }
    public long? FeeCents { get; set; }
    public string? ParentId { get; set; }
    public string? IdempotencyId { get; set; }
    public int? OrderId { get; set; }
    public bool Matched { get; set; }
    public string? MatchReason { get; set; }
}

public class ReconciliationOrder
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public long TotalCents { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string FulfilmentStatus { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public long RefundedAmountCents { get; set; }
    public bool Matched { get; set; }
    public List<string> FinixTransactionIds { get; set; } = new();
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int FinixTransactionCount { get; set; }
    public int OrderCount { get; set; }
    public List<ReconciliationFinixTransaction> FinixTransactions { get; set; } = new();
    public List<ReconciliationOrder> Orders { get; set; } = new();
    public List<ReconciliationFinixTransaction> UnmatchedFinixTransactions { get; set; } = new();
    public List<ReconciliationOrder> UnmatchedOrders { get; set; } = new();
}
