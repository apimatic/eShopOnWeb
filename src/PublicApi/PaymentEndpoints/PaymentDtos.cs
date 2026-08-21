using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

// ----- Shared card / address input -------------------------------------------------------

/// <summary>Billing address for a card. Never persisted by this app.</summary>
public class BillingAddressDto
{
    public string? Line1 { get; set; }
    public string? Line2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "US";
}

/// <summary>Raw card input. Never persisted and never logged — forwarded straight to PayPal.</summary>
public class CardDto
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;   // "YYYY-MM"
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public BillingAddressDto? BillingAddress { get; set; }
}

/// <summary>Shipping address for an order.</summary>
public class ShippingAddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

// ----- Order creation --------------------------------------------------------------------

public class CreateOrderItemDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderItemDto> Items { get; set; } = new();
    public ShippingAddressDto? ShipToAddress { get; set; }
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }
    public CreateOrderResponse() { }

    /// <summary>Top-level identifier of the created order.</summary>
    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
}

// ----- Pay -------------------------------------------------------------------------------

public class PayOrderRequest : BaseRequest
{
    /// <summary>Raw card for a one-off payment. Mutually exclusive with <see cref="SavedPaymentMethodId"/>.</summary>
    public CardDto? Card { get; set; }

    /// <summary>Id of one of the caller's saved cards to pay with instead.</summary>
    public int? SavedPaymentMethodId { get; set; }
}

// ----- Refund ----------------------------------------------------------------------------

public class RefundOrderRequest : BaseRequest
{
    /// <summary>Amount to refund. Null = refund the full remaining captured amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key — repeating a request under the same key never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    /// <summary>Top-level identifier of the refund.</summary>
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Status { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal RemainingRefundable { get; set; }
}

// ----- Payment state (shared response shape) --------------------------------------------

public class RefundDto
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class PaymentStateDto
{
    public string? Currency { get; set; }
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal TotalRefunded { get; set; }
    public decimal RemainingRefundable { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();
}

public class OrderPaymentResponse : BaseResponse
{
    public OrderPaymentResponse(Guid correlationId) : base(correlationId) { }
    public OrderPaymentResponse() { }

    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public PaymentStateDto? Payment { get; set; }
}

public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public PaymentStateDto? Payment { get; set; }
}

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(Guid correlationId) : base(correlationId) { }
    public MyOrdersResponse() { }

    public List<OrderSummaryDto> Orders { get; set; } = new();
}

// ----- Saved cards -----------------------------------------------------------------------

public class SavePaymentMethodRequest : BaseRequest
{
    public CardDto Card { get; set; } = new();
    public string? Alias { get; set; }
}

public class SavePaymentMethodResponse : BaseResponse
{
    public SavePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public SavePaymentMethodResponse() { }

    /// <summary>Top-level identifier of the saved card.</summary>
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }
    public string? Alias { get; set; }
}

public class SavedCardDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }
    public string? Alias { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public ListPaymentMethodsResponse(Guid correlationId) : base(correlationId) { }
    public ListPaymentMethodsResponse() { }

    public List<SavedCardDto> PaymentMethods { get; set; } = new();
}

// ----- Reconciliation --------------------------------------------------------------------

public class ReconciliationTransactionDto
{
    public string? TransactionId { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? CurrencyCode { get; set; }
    public string? Fee { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public string? ReferenceId { get; set; }
    public int? MatchedOrderId { get; set; }
}

public class ReconciliationOrderDto
{
    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public string? CaptureId { get; set; }
    public decimal? CapturedAmount { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int PayPalTransactionCount { get; set; }
    public int MatchedCount { get; set; }

    /// <summary>Transactions PayPal knows about that did not line up with an eShop order.</summary>
    public List<ReconciliationTransactionDto> InPayPalNotInEShop { get; set; } = new();

    /// <summary>eShop orders (paid in the range) with no matching PayPal transaction.</summary>
    public List<ReconciliationOrderDto> InEShopNotInPayPal { get; set; } = new();

    /// <summary>Transactions that matched an eShop order.</summary>
    public List<ReconciliationTransactionDto> Matched { get; set; } = new();
}
