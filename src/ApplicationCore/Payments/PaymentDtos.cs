using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed class OrderItemInput
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public sealed class ShippingAddressInput
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public sealed class CardInput
{
    public string Number { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public FinixAddress? BillingAddress { get; set; }
}

public sealed class PayOrderInput
{
    /// <summary>The local id of one of the caller's saved cards.</summary>
    public int? PaymentMethodId { get; set; }

    /// <summary>One-off card details. Never stored by eShopOnWeb.</summary>
    public CardInput? Card { get; set; }
}

public sealed class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
}

public sealed class RefundDto
{
    public int RefundId { get; set; }
    public string FinixRefundId { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public long AmountInCents { get; set; }
    public decimal Amount { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class PaymentDto
{
    public string Status { get; set; } = string.Empty;
    public long AmountInCents { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? AuthorizationState { get; set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; set; }
    public string? CaptureTransferId { get; set; }
    public string? CaptureState { get; set; }
    public long? CapturedAmountInCents { get; set; }
    public decimal? CapturedAmount { get; set; }
    public long? FinixFeeInCents { get; set; }
    public decimal? FinixFee { get; set; }
    public long? NetAmountInCents { get; set; }
    public decimal? NetAmount { get; set; }
    public long RefundedAmountInCents { get; set; }
    public decimal RefundedAmount { get; set; }
    public long? RefundableAmountInCents { get; set; }
    public decimal? RefundableAmount { get; set; }
    public List<RefundDto> Refunds { get; set; } = new();
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public DateTimeOffset? AuthorizedAt { get; set; }
    public DateTimeOffset? CapturedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
}

public sealed class OrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public long AmountInCents { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public PaymentDto Payment { get; set; } = new();
}

public sealed class SavedCardDto
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? CardholderName { get; set; }
    public bool Enabled { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class RefundResult
{
    public int RefundId { get; set; }
    public string FinixRefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public long AmountInCents { get; set; }
    public decimal Amount { get; set; }
    public long RefundedAmountInCents { get; set; }
    public long RefundableAmountInCents { get; set; }
    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
}

public sealed class ReconciliationTransactionDto
{
    public string Id { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? State { get; set; }
    public long AmountInCents { get; set; }
    public long FeeInCents { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string? Source { get; set; }
    public string? ParentTransfer { get; set; }
    public string? IdempotencyId { get; set; }
    public Dictionary<string, string>? Tags { get; set; }
}

public sealed class ReconciliationMatchDto
{
    public string Kind { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string? FinixTransactionId { get; set; }
    public long AmountInCents { get; set; }
    public string? EshopState { get; set; }
}

public sealed class ReconciliationMismatchDto
{
    public string Kind { get; set; } = string.Empty;
    public int? OrderId { get; set; }
    public string? FinixTransactionId { get; set; }
    public long AmountInCents { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string? Detail { get; set; }
}

public sealed class ReconciliationReportDto
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationTransactionDto> FinixTransactions { get; set; } = new();
    public List<ReconciliationMatchDto> Matched { get; set; } = new();
    public List<ReconciliationMismatchDto> UnmatchedFinixTransactions { get; set; } = new();
    public List<ReconciliationMismatchDto> UnmatchedEshopRecords { get; set; } = new();
}
