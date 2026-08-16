using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.PayPal;

// ---- Inputs ----

/// <summary>Raw card details for a one-off payment or for vaulting. Never persisted or logged.</summary>
public record CardDetails(
    string Number,
    string Expiry,           // "YYYY-MM"
    string? SecurityCode,
    string? Name,
    BillingAddressDetails? BillingAddress);

public record BillingAddressDetails(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea1,      // state / province
    string? AdminArea2,      // city
    string? PostalCode,
    string? CountryCode);

public record PayPalLineItem(string Name, string UnitAmount, int Quantity);

/// <summary>Everything the gateway needs to place a hold (authorization) for an order.</summary>
public record CreateAuthorizationCommand(
    string ReferenceId,      // eShop order id, as a string
    string InvoiceId,        // stable per order — dedupes the authorization at PayPal
    string Amount,           // e.g. "12.34" (equals the order total to the cent)
    string CurrencyCode,
    IReadOnlyList<PayPalLineItem> Items,
    CardDetails? Card,       // one-off card ...
    string? VaultId);        // ... or a saved-card vault token (exactly one is set)

// ---- Results ----

public record PayPalAuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    string Amount,
    string CurrencyCode);

/// <summary>Live view of an authorization (used to detect a stale/expired hold before capture).</summary>
public record PayPalAuthorizationDetails(
    string AuthorizationId,
    string Status,           // CREATED, PENDING, CAPTURED, VOIDED, EXPIRED
    decimal Amount,
    string CurrencyCode);

public record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal PayPalFee,
    decimal NetAmount,
    string CurrencyCode);

public record PayPalRefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string CurrencyCode);

public record PayPalVaultedCard(
    string VaultId,
    string? CustomerId,
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? Name);

/// <summary>One record from PayPal's transaction-search (reporting) API.</summary>
public record PayPalTransaction(
    string TransactionId,
    string? Status,
    decimal? Amount,
    string? CurrencyCode,
    DateTimeOffset? Date,
    string? InvoiceId,
    string? CustomField);
