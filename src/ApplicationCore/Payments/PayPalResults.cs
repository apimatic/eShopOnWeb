using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>Result of creating a PayPal order.</summary>
public record PayPalOrderResult(string OrderId, string Status);

/// <summary>An authorization (hold) as PayPal reports it.</summary>
public record PayPalAuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

/// <summary>A capture as PayPal reports it, including the fee/net breakdown.</summary>
public record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal PayPalFee,
    decimal NetAmount,
    string CurrencyCode);

/// <summary>A refund as PayPal reports it.</summary>
public record PayPalRefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string CurrencyCode);

/// <summary>A card successfully vaulted with PayPal, described safely for the shopper.</summary>
public record VaultedCard(
    string VaultId,
    string Brand,
    string Last4,
    string Expiry,
    string? CardholderName);

/// <summary>A transaction as PayPal's Transaction Search reports it (for reconciliation).</summary>
public record PayPalTransaction(
    string TransactionId,
    string? InvoiceId,
    string? CustomField,
    decimal Amount,
    decimal Fee,
    string CurrencyCode,
    string Status,
    string EventCode,
    DateTimeOffset InitiationDate);
