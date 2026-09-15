using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

/// <summary>
/// Raw card details for a one-off payment or for vaulting. This is a transient input only:
/// it is never persisted in the application database and never written to logs.
/// </summary>
public record CardDetails(
    string Number,
    string Expiry,          // YYYY-MM
    string? SecurityCode,
    string CardholderName,
    string? BillingAddressLine1,
    string? BillingCity,
    string? BillingState,
    string? BillingPostalCode,
    string? BillingCountryCode);

/// <summary>Result of authorizing (holding) an order total with PayPal.</summary>
public record PayPalAuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string AuthorizationStatus,
    DateTimeOffset? ExpiresAt,
    string OrderStatus,
    bool RequiresPayerAction);

/// <summary>Result of reauthorizing a stale hold (a fresh authorization).</summary>
public record PayPalReauthorizationResult(
    string AuthorizationId,
    string AuthorizationStatus,
    DateTimeOffset? ExpiresAt);

/// <summary>Result of capturing a hold (taking the money) at fulfilment.</summary>
public record PayPalCaptureResult(
    string CaptureId,
    string CaptureStatus,
    decimal GrossAmount,
    decimal PayPalFee,
    decimal NetAmount,
    string CurrencyCode);

/// <summary>Result of refunding a captured payment.</summary>
public record PayPalRefundResult(
    string RefundId,
    string RefundStatus,
    decimal Amount,
    string CurrencyCode);

/// <summary>Result of vaulting a card (saving it for reuse).</summary>
public record PayPalVaultResult(
    string VaultId,
    string? CustomerId,
    string Brand,
    string Last4,
    string? Expiry);

/// <summary>One row of PayPal's own transaction record, for reconciliation.</summary>
public record PayPalTransaction(
    string TransactionId,
    string? TransactionStatus,
    string? EventCode,
    decimal Amount,
    decimal FeeAmount,
    string? CurrencyCode,
    DateTimeOffset? InitiationDate,
    string? InvoiceId,
    string? CustomField);
