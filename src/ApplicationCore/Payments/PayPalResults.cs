using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>Result of authorizing (or reauthorizing) an order total: the hold PayPal placed.</summary>
public sealed record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

/// <summary>Result of capturing a hold at fulfilment, with the money figures PayPal reported.</summary>
public sealed record CaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal PayPalFee,
    decimal NetAmount);

/// <summary>Result of refunding a capture (full or partial).</summary>
public sealed record RefundResult(
    string RefundId,
    string Status,
    decimal Amount);

/// <summary>A card saved into PayPal's vault, described safely (no full card number).</summary>
public sealed record VaultedCardResult(
    string VaultId,
    string? Last4,
    string? Brand,
    int? ExpiryMonth,
    int? ExpiryYear);

/// <summary>One transaction from PayPal's own reporting, used to reconcile against eShop orders.</summary>
public sealed record ReconciliationTransaction(
    string TransactionId,
    string Status,
    decimal Amount,
    string CurrencyCode,
    DateTimeOffset? Date,
    string? EventCode);
