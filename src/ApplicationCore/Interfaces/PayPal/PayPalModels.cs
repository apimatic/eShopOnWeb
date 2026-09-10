using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

/// <summary>
/// Raw card details for a one-off payment or for vaulting. This type is transient: it is never
/// persisted by the application and never written to logs. Only PayPal ever sees the number.
/// </summary>
public record CardDetails(
    string Number,
    string Expiry,
    string? SecurityCode,
    string? CardholderName,
    CardBillingAddress? BillingAddress);

public record CardBillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea1,
    string? AdminArea2,
    string? PostalCode,
    string? CountryCode);

/// <summary>Result of authorizing an order at PayPal (the hold on the money).</summary>
public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

/// <summary>Result of capturing an authorization (the money actually taken).</summary>
public record CaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal PayPalFee,
    decimal NetAmount);

/// <summary>Result of reauthorizing a stale authorization (a new hold).</summary>
public record ReauthorizeResult(
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

public record RefundResult(string RefundId, string Status);

/// <summary>Safe-to-display details of a vaulted card (never the full number).</summary>
public record VaultCardResult(
    string VaultId,
    string Brand,
    string Last4,
    string Expiry,
    string? CardholderName);

/// <summary>One transaction from PayPal's transaction reporting, projected to what we reconcile on.</summary>
public record PayPalTransaction(
    string TransactionId,
    string? EventCode,
    string? Status,
    decimal Amount,
    string Currency,
    decimal? Fee,
    DateTimeOffset? InitiatedAt,
    string? CustomField,
    string? InvoiceId,
    string? ReferenceId);
