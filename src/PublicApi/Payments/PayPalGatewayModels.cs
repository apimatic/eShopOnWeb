using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>Raw card details for a one-off payment or a card to be vaulted. Never persisted or logged.</summary>
public sealed record CardDetails(
    string Number,
    string Expiry,
    string SecurityCode,
    string? CardholderName,
    BillingAddress? BillingAddress);

public sealed record BillingAddress(
    string CountryCode,
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea1,
    string? AdminArea2,
    string? PostalCode);

/// <summary>Instruction to place a hold (authorization) for an order's total.</summary>
public sealed record AuthorizeInstruction(
    int OrderId,
    decimal Amount,
    string InvoiceId,
    CardDetails? OneOffCard,
    string? VaultId,
    string? InstrumentDescriptor);

public sealed record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt,
    string? InstrumentDescriptor);

/// <summary>Instruction to capture (take) an authorized payment at fulfilment.</summary>
public sealed record CaptureInstruction(
    int OrderId,
    string InvoiceId,
    string PayPalOrderId,
    string AuthorizationId,
    decimal Amount,
    DateTimeOffset? AuthorizationExpiresAt);

public sealed record CaptureResult(
    string CaptureId,
    string Status,
    decimal Gross,
    decimal? PayPalFee,
    decimal? NetAmount,
    string? RenewedAuthorizationId,
    DateTimeOffset? RenewedExpiresAt);

/// <summary>Instruction to refund a captured payment, in full (Amount == null) or in part.</summary>
public sealed record RefundInstruction(
    int OrderId,
    string CaptureId,
    decimal? Amount,
    string IdempotencyKey,
    string InvoiceId);

public sealed record RefundResult(
    string RefundId,
    string Status,
    decimal Amount);

public sealed record VaultCardInstruction(
    CardDetails Card,
    string CustomerId);

public sealed record VaultResult(
    string VaultId,
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? CardholderName);

/// <summary>A single transaction as PayPal's own reporting knows it, for reconciliation.</summary>
public sealed record PayPalTransaction(
    string? TransactionId,
    string? InvoiceId,
    decimal? Amount,
    string? CurrencyCode,
    decimal? FeeAmount,
    string? Status,
    string? InitiatedDate,
    string? EventCode);

/// <summary>The raw result of walking PayPal's transaction reporting for a date range.</summary>
public sealed record TransactionSearchResult(
    IReadOnlyList<PayPalTransaction> Transactions,
    int PagesRead,
    int TotalPages,
    bool CoveredAllPages);
