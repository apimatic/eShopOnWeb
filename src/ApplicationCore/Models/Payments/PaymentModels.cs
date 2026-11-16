using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Payments;

public sealed class OrderLineInput
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public sealed class CardAddressInput
{
    public string? Line1 { get; set; }
    public string? Line2 { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
}

/// <summary>
/// One-off card details supplied by a caller. These values are sent to Finix and are never
/// persisted or logged by this application.
/// </summary>
public sealed class CardInput
{
    public string Number { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string SecurityCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public CardAddressInput? Address { get; set; }
}

public sealed class OrderItemView
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public sealed class OrderSummary
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public long TotalCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public List<OrderItemView> Items { get; set; } = new();
}

public sealed class RefundView
{
    public int RefundId { get; set; }
    public long AmountCents { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ProviderState { get; set; }
    public string? FinixTransferId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class PaymentView
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public long AmountCents { get; set; }
    public decimal Amount { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationState { get; set; }
    public string? CaptureTransferId { get; set; }
    public string? CaptureState { get; set; }
    public long? CapturedAmountCents { get; set; }
    public decimal? CapturedAmount { get; set; }
    public long? FeeCents { get; set; }
    public decimal? Fee { get; set; }
    public long? NetCents { get; set; }
    public decimal? Net { get; set; }
    public long RefundedAmountCents { get; set; }
    public decimal RefundedAmount { get; set; }
    public long RefundableAmountCents { get; set; }
    public decimal RefundableAmount { get; set; }
    public string? CardBrand { get; set; }
    public string? CardLast4 { get; set; }
    public int? SavedPaymentMethodId { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public List<RefundView> Refunds { get; set; } = new();
}

public sealed class OrderView
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public long TotalCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public List<OrderItemView> Items { get; set; } = new();
    public PaymentView? Payment { get; set; }
}

public sealed class SavedPaymentMethodView
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string CardholderName { get; set; } = string.Empty;
    public string FinixPaymentInstrumentId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
