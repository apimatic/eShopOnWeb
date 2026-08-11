using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>Raw card details for a one-off payment or for vaulting. Never persisted by this app.</summary>
public record PayPalCardDetails(
    string Number,
    string Expiry,
    string SecurityCode,
    string? Name,
    string? BillingAddressLine1 = null,
    string? BillingAddressLine2 = null,
    string? AdminArea1 = null,
    string? AdminArea2 = null,
    string? PostalCode = null,
    string? CountryCode = null);

/// <summary>A payment source to authorize an order with: either a one-off card or a saved (vaulted) card.</summary>
public abstract record PayPalPaymentSource;

public sealed record CardPaymentSource(PayPalCardDetails Card) : PayPalPaymentSource;

public sealed record VaultedCardPaymentSource(string VaultId) : PayPalPaymentSource;

/// <summary>Result of authorizing an order: PayPal's ids and status for the hold.</summary>
public record PayPalAuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt,
    string? InstrumentDescription);

/// <summary>Result of capturing an authorization: what PayPal reported for the settled money.</summary>
public record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal Gross,
    decimal? Fee,
    decimal? Net,
    string CurrencyCode);

/// <summary>Result of renewing a stale authorization.</summary>
public record PayPalReauthorizeResult(
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

/// <summary>Result of refunding a capture.</summary>
public record PayPalRefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string CurrencyCode);

/// <summary>Result of vaulting a card: the durable token plus a safe description.</summary>
public record PayPalVaultCardResult(
    string VaultId,
    string CustomerId,
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? CardholderName);

/// <summary>One row of PayPal's own transaction record, used for reconciliation.</summary>
public record PayPalTransaction(
    string TransactionId,
    string? EventCode,
    string? Status,
    decimal Amount,
    decimal? Fee,
    string CurrencyCode,
    DateTimeOffset? Date,
    string? InvoiceId,
    string? CustomField);
