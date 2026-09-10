using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

/// <summary>
/// Provider-neutral inputs and results for talking to PayPal. These keep PayPal's JSON wire-format
/// confined to the Infrastructure implementation; the application layer works only with these types.
/// </summary>

public record PayPalBillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,
    string? AdminArea1,
    string? PostalCode,
    string CountryCode);

/// <summary>Raw one-off card details. Never persisted or logged by this app.</summary>
public record PayPalRawCard(
    string Number,
    string Expiry,          // ISO-8601 YYYY-MM
    string? SecurityCode,
    string? Name,
    PayPalBillingAddress? BillingAddress);

/// <summary>
/// The funding instrument for an authorization: either raw card details for a one-off payment, or the
/// vault id of one of the shopper's saved cards. Exactly one is set.
/// </summary>
public record PayPalCardPaymentSource(PayPalRawCard? Card, string? VaultId);

/// <summary>Everything needed to create the PayPal order that holds the funds.</summary>
public record CreateOrderCommand(
    decimal Amount,
    string CurrencyCode,
    string InvoiceId,
    string CustomId,
    string? Description);

public record PayPalOrderResult(string Id, string Status);

public record PayPalAuthorizationResult(string Id, string Status, DateTimeOffset? ExpiresAt);

public record PayPalCaptureResult(
    string Id,
    string Status,
    decimal GrossAmount,
    decimal? PayPalFee,
    decimal? NetAmount);

public record PayPalRefundResult(string Id, string Status, decimal Amount);

/// <summary>The safe, non-sensitive description of a vaulted card returned by PayPal.</summary>
public record PayPalVaultCardResult(string VaultId, string? Brand, string? Last4, string? Expiry);

/// <summary>A single row from PayPal's transaction report, projected to what reconciliation needs.</summary>
public record PayPalTransaction(
    string TransactionId,
    string? EventCode,
    string? Status,
    decimal? GrossAmount,
    decimal? FeeAmount,
    string? CurrencyCode,
    DateTimeOffset? InitiationDate,
    string? InvoiceId,
    string? CustomField);
