using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>The outcome of placing an order.</summary>
public record PlacedOrder(int OrderId, decimal Amount, string Currency);

/// <summary>A refund as surfaced to the API.</summary>
public record RefundView(string? RefundId, decimal Amount, string? Status);

/// <summary>An order together with its payment/fulfilment state, as surfaced to the API.</summary>
public record OrderPaymentView
{
    public int OrderId { get; init; }
    public DateTimeOffset OrderDate { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Currency { get; init; } = string.Empty;
    public decimal Amount { get; init; }

    public string? PayPalOrderId { get; init; }
    public string? AuthorizationId { get; init; }
    public string? AuthorizationStatus { get; init; }
    public string? AuthorizationExpiresAt { get; init; }
    public string? CaptureId { get; init; }
    public string? CaptureStatus { get; init; }
    public decimal? CapturedAmount { get; init; }
    public decimal? PayPalFee { get; init; }
    public decimal? NetAmount { get; init; }
    public decimal RefundedAmount { get; init; }
    public decimal RefundableRemaining { get; init; }

    public IReadOnlyList<RefundView> Refunds { get; init; } = Array.Empty<RefundView>();
}

/// <summary>A saved card as surfaced to the API — never full card details.</summary>
public record SavedCardView(int PaymentMethodId, string? CardBrand, string? LastFourDigits, string? Expiry,
    string? CardholderName, DateTimeOffset CreatedDate);

/// <summary>How a transaction lines up between PayPal and eShop.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum ReconciliationMatch
{
    Matched = 0,
    PayPalOnly = 1,
    EShopOnly = 2
}

/// <summary>One reconciliation line, pairing a PayPal transaction with an eShop order where possible.</summary>
public record ReconciliationEntry
{
    public ReconciliationMatch Match { get; init; }
    public string? InvoiceReference { get; init; }

    public string? PayPalTransactionId { get; init; }
    public string? PayPalReferenceId { get; init; }
    public decimal? PayPalAmount { get; init; }
    public string? PayPalStatus { get; init; }

    public int? EShopOrderId { get; init; }
    public decimal? EShopCapturedAmount { get; init; }
    public string? EShopStatus { get; init; }
}

/// <summary>The reconciliation report over a date range.</summary>
public record ReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public int PayPalTransactionCount { get; init; }
    public int MatchedCount { get; init; }
    public int PayPalOnlyCount { get; init; }
    public int EShopOnlyCount { get; init; }
    public IReadOnlyList<ReconciliationEntry> Entries { get; init; } = Array.Empty<ReconciliationEntry>();
}
