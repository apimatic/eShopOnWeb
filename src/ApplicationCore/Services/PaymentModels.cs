using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public sealed class CreateOrderCommand
{
    public List<CreateOrderItem> Items { get; set; } = new();
    public ShippingAddressCommand? ShippingAddress { get; set; }
}

public sealed class CreateOrderItem
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public sealed class ShippingAddressCommand
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public sealed class CardCommand
{
    public string Number { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public string Name { get; set; } = string.Empty;
    public ShippingAddressCommand Address { get; set; } = new();
}

public sealed class PayOrderCommand
{
    public int? PaymentMethodId { get; set; }
    public CardCommand? Card { get; set; }
}

public sealed class SavePaymentMethodCommand
{
    public CardCommand Card { get; set; } = new();
    public string? Alias { get; set; }
}

public sealed class RefundCommand
{
    public decimal? Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class OrderSummary
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public long TotalCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public ShippingAddressSummary ShipToAddress { get; set; } = new();
    public List<OrderItemSummary> Items { get; set; } = new();
    public PaymentSummary? Payment { get; set; }
}

public sealed class ShippingAddressSummary
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public sealed class OrderItemSummary
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
    public decimal LineTotal { get; set; }
}

public sealed class PaymentSummary
{
    public int? PaymentId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Currency { get; set; }
    public long AmountCents { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationState { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public bool IsVoided { get; set; }
    public string? CaptureTransferId { get; set; }
    public string? CaptureState { get; set; }
    public long? CapturedAmountCents { get; set; }
    public decimal? FeeCents { get; set; }
    public decimal? NetCents { get; set; }
    public bool FeePending { get; set; }
    public long RefundedAmountCents { get; set; }
    public long RefundableAmountCents { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public List<RefundSummary> Refunds { get; set; } = new();
}

public sealed class RefundSummary
{
    public int RefundId { get; set; }
    public string? FinixRefundId { get; set; }
    public long AmountCents { get; set; }
    public string? State { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class SavedCardSummary
{
    public int PaymentMethodId { get; set; }
    public string? Alias { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ReconciliationReport
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationEntry> FinixTransactions { get; set; } = new();
    public List<ReconciliationEntry> EshopOnlyTransactions { get; set; } = new();
    public ReconciliationSummary Summary { get; set; } = new();
}

public sealed class ReconciliationEntry
{
    public string Source { get; set; } = string.Empty;
    public string TransactionType { get; set; } = string.Empty;
    public string FinixId { get; set; } = string.Empty;
    public int? OrderId { get; set; }
    public long AmountCents { get; set; }
    public string? Currency { get; set; }
    public string? State { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public bool Matched { get; set; }
    public string MatchStatus { get; set; } = string.Empty;
}

public sealed class ReconciliationSummary
{
    public int FinixAuthorizationCount { get; set; }
    public int FinixTransferCount { get; set; }
    public int MatchedCount { get; set; }
    public int FinixOnlyCount { get; set; }
    public int EshopOnlyCount { get; set; }
}
