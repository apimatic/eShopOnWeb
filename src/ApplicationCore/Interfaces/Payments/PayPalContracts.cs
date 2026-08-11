using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>Raw card details for a one-off payment or to vault. Never persisted or logged by this app.</summary>
public record CardDetails(
    string Number,
    int ExpiryMonth,
    int ExpiryYear,
    string SecurityCode,
    string? CardholderName,
    string? BillingAddressLine1,
    string? BillingAddressLine2,
    string? BillingCity,
    string? BillingState,
    string? BillingPostalCode,
    string? BillingCountryCode)
{
    /// <summary>PayPal expects expiry as ISO YYYY-MM.</summary>
    public string ToPayPalExpiry() => $"{ExpiryYear:D4}-{ExpiryMonth:D2}";
}

/// <summary>What to authorize: an amount, reconciliation ids, and exactly one of a raw card or a saved-card vault id.</summary>
public record AuthorizeRequest(
    decimal Amount,
    string CurrencyCode,
    string RequestId,
    string InvoiceId,
    string CustomId,
    CardDetails? Card,
    string? VaultId);

public record AuthorizeResult(
    string PayPalOrderId,
    string OrderStatus,
    string? AuthorizationId,
    string? AuthorizationStatus,
    DateTimeOffset? ExpiresAt,
    string? InstrumentDescription,
    bool RequiresPayerAction);

public record AuthorizationDetails(
    string Id,
    string Status,
    DateTimeOffset? ExpiresAt);

public record ReauthorizeResult(
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

public record CaptureResult(
    string CaptureId,
    string Status,
    decimal Gross,
    decimal? Fee,
    decimal? Net,
    string CurrencyCode);

public record RefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string CurrencyCode);

public record VaultCardResult(
    string VaultId,
    string CustomerId,
    string Brand,
    string Last4,
    string Expiry,
    string? CardholderName);

/// <summary>A transaction as PayPal's own reporting knows it, for reconciliation against eShop orders.</summary>
public record PayPalTransaction(
    string TransactionId,
    string? EventCode,
    DateTimeOffset? InitiationDate,
    decimal? Amount,
    string? CurrencyCode,
    decimal? Fee,
    string? Status,
    string? InvoiceId,
    string? CustomField,
    string? ReferenceId);
