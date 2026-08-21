using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>A billing address for a card. Plain data passed to the gateway; never persisted.</summary>
public record PayPalBillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    string CountryCode);

/// <summary>
/// Raw card details for a one-off card payment or for vaulting. Passed to the gateway and then
/// discarded — never stored by the app and never logged.
/// </summary>
public record PayPalCardData(
    string Number,
    string Expiry,          // YYYY-MM
    string SecurityCode,
    string? CardholderName,
    PayPalBillingAddress? BillingAddress);

/// <summary>An authorization request: the amount to hold, paid with a raw card or a saved vault id.</summary>
public record PayPalAuthorizationRequest(
    decimal Amount,
    string Currency,
    string OrderReference,
    PayPalCardData? Card,
    string? VaultId);

public record AuthorizationResult(string PayPalOrderId, string AuthorizationId, string? Status);

public record CaptureResult(
    string CaptureId,
    string? Status,
    decimal CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string Currency);

public record ReauthorizationResult(string AuthorizationId, string? Status);

public record RefundResult(string RefundId, string? Status, decimal Amount);

public record VaultCardResult(
    string VaultId,
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? CardholderName);

/// <summary>One transaction from PayPal's reporting, used to line up against eShop orders.</summary>
public record ReconciliationTransaction(
    string? TransactionId,
    string? Status,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    DateTimeOffset? InitiatedDate,
    DateTimeOffset? UpdatedDate,
    string? InvoiceId,
    string? CustomField);

/// <summary>A line in an order-placement request: a catalog item and how many of it.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>A transaction PayPal reports that matches an eShop captured order.</summary>
public record ReconciliationMatch(
    int OrderId,
    string CaptureId,
    decimal? PayPalAmount,
    decimal EshopAmount,
    string? PayPalStatus,
    string EshopStatus);

/// <summary>An eShop captured order with no matching PayPal transaction in the range.</summary>
public record ReconciliationEshopEntry(int OrderId, string CaptureId, decimal Amount, string Status);

/// <summary>
/// Reconciliation over a date range: transactions present in both systems, transactions PayPal
/// knows about but eShop does not, and captured orders eShop knows about but PayPal did not return.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<ReconciliationTransaction> InPayPalOnly,
    IReadOnlyList<ReconciliationEshopEntry> InEshopOnly);
