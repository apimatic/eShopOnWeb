using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>
/// Plain data the application hands the gateway to fund a payment. Card details, when present, are used
/// for a single call and never persisted or logged by the application.
/// </summary>
public record CardInput(
    string Number,
    string Expiry,          // ISO-8601 YYYY-MM
    string SecurityCode,
    string? CardholderName,
    BillingAddressInput? BillingAddress = null);

public record BillingAddressInput(
    string? AddressLine1,
    string? AdminArea2,     // city
    string? AdminArea1,     // state
    string? PostalCode,
    string? CountryCode);

/// <summary>Authorize a card payment for an order: either raw <paramref name="Card"/> or a saved <paramref name="VaultId"/>.</summary>
public record AuthorizeCommand(
    decimal Amount,
    string CurrencyCode,
    string ReferenceId,
    string Description,
    string IdempotencyKeyBase,
    CardInput? Card,
    string? VaultId);

public record GatewayAuthorization(
    string PayPalOrderId,
    string AuthorizationId,
    string? Status,
    DateTimeOffset? ExpiresAt,
    decimal? Amount,
    string CurrencyCode);

/// <summary>Lightweight current state of an authorization (for settle/re-read paths).</summary>
public record GatewayAuthorizationState(
    string AuthorizationId,
    string? Status,
    DateTimeOffset? ExpiresAt);

public record GatewayCapture(
    string CaptureId,
    string? Status,
    decimal? CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string CurrencyCode);

public record GatewayRefund(
    string RefundId,
    string? Status,
    decimal? Amount,
    string CurrencyCode);

public record VaultCardCommand(
    CardInput Card,
    string? CustomerId,
    string IdempotencyKey);

public record GatewayVaultedCard(
    string VaultId,
    string? Brand,
    string? Last4,
    string? Expiry,
    string? CardholderName,
    string? CustomerId);

public record GatewayTransaction(
    string? TransactionId,
    string? Status,
    decimal? Amount,
    string? CurrencyCode,
    string? InvoiceId,
    string? CustomField,
    DateTimeOffset? InitiatedAt);

/// <summary>Result of a full (all-pages) transaction search over a date range.</summary>
public record TransactionSearchResult(
    IReadOnlyList<GatewayTransaction> Transactions,
    int PagesScanned,
    int TotalPages,
    bool Complete);
