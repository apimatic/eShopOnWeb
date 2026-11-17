using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public class CreateOrderRequest
{
    public List<CreateOrderItem> Items { get; set; } = new();
    public ShippingAddressRequest? ShippingAddress { get; set; }
}

public class CreateOrderItem
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

public class CardRequest
{
    public string Name { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public ShippingAddressRequest? BillingAddress { get; set; }
}

public class PayOrderRequest
{
    public int? PaymentMethodId { get; set; }
    public CardRequest? Card { get; set; }
}

public class SavePaymentMethodRequest
{
    public CardRequest Card { get; set; } = new();
    public string? Alias { get; set; }
}

public class RefundOrderRequest
{
    /// <summary>
    /// Amount to refund in the configured currency. When omitted the entire
    /// remaining captured amount is refunded.
    /// </summary>
    public decimal? Amount { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;
}

public class OrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public List<OrderItemDto> Items { get; set; } = new();
    public PaymentDto? Payment { get; set; }
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class PaymentDto
{
    public string? AuthorizationId { get; set; }
    public string? AuthorizationState { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public string? CaptureTransferId { get; set; }
    public string? CaptureState { get; set; }
    public decimal AuthorizedAmount { get; set; }
    public decimal CapturedAmount { get; set; }
    public decimal Fee { get; set; }
    public decimal Net { get; set; }
    public decimal RefundedAmount { get; set; }
    public decimal RefundableAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();
}

public class RefundDto
{
    public int RefundId { get; set; }
    public string? FinixRefundId { get; set; }
    public string State { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string? FailureMessage { get; set; }
}

public class SavedPaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string Last4 { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? CardholderName { get; set; }
    public string? Alias { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class ReconciliationReport
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int MatchedCount { get; set; }
    public int FinixOnlyCount { get; set; }
    public int EshopOnlyCount { get; set; }
    public List<ReconciliationEntry> Entries { get; set; } = new();
}

public class ReconciliationEntry
{
    public string Status { get; set; } = string.Empty;
    public string? FinixTransferId { get; set; }
    public string? FinixType { get; set; }
    public string? FinixOperationKey { get; set; }
    public string? FinixState { get; set; }
    public decimal? FinixAmount { get; set; }
    public int? OrderId { get; set; }
    public string? OrderStatus { get; set; }
    public string? EshopReferenceType { get; set; }
    public decimal? EshopAmount { get; set; }
    public string? Note { get; set; }
}
