using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.PayPal;

/// <summary>Card details for a one-off card payment or a vault save. Never persisted or logged.</summary>
public record PayPalCardDetails(
    string Number,
    string Expiry,           // YYYY-MM
    string SecurityCode,
    string? CardholderName,
    PayPalBillingAddress? BillingAddress);

/// <summary>A billing address for a card.</summary>
public record PayPalBillingAddress(
    string? AddressLine1,
    string? AdminArea2,      // city
    string? AdminArea1,      // state/province
    string? PostalCode,
    string? CountryCode);    // ISO-3166-1 alpha-2

/// <summary>
/// A request to authorize (hold) an order total. Exactly one of <see cref="Card"/> or
/// <see cref="VaultId"/> funds the payment.
/// </summary>
public record PayPalAuthorizeRequest(
    string OrderReference,   // eShop order reference; set as PayPal custom_id (used for reconciliation)
    string InvoiceId,        // unique per order; set as PayPal invoice_id (account requires uniqueness)
    string Currency,
    decimal Amount,
    string Description,
    string IdempotencyKey,   // stable per order, so a double-click reuses the same hold
    PayPalCardDetails? Card,
    string? VaultId);

/// <summary>The outcome of an authorization.</summary>
public record PayPalAuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string AuthorizationStatus,
    DateTimeOffset? ExpiresAt);

/// <summary>A read-back of an authorization's current state.</summary>
public record PayPalAuthorizationSnapshot(
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

/// <summary>
/// A read-back of a PayPal order's status and (if present) its authorization and capture — used
/// to settle an unknown outcome after a transport failure.
/// </summary>
public record PayPalOrderSnapshot(
    string PayPalOrderId,
    string Status,
    string? AuthorizationId,
    string? AuthorizationStatus,
    DateTimeOffset? ExpiresAt,
    PayPalCaptureResult? Capture);

/// <summary>The settlement figures PayPal reports for a capture.</summary>
public record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal Gross,
    decimal? PaypalFee,
    decimal? Net,
    string Currency);

/// <summary>The outcome of a refund.</summary>
public record PayPalRefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

/// <summary>A request to vault (save) a card for a shopper.</summary>
public record PayPalVaultCardRequest(
    PayPalCardDetails Card,
    string? ExistingPayPalCustomerId);   // reuse the shopper's customer id if we already have one

/// <summary>A vaulted (saved) card, described safely — never full card details.</summary>
public record PayPalVaultedCard(
    string PaymentTokenId,
    string? PayPalCustomerId,
    string? Brand,
    string? LastFourDigits,
    string? Expiry,
    string? CardholderName);

/// <summary>A single PayPal transaction as reported by transaction search.</summary>
public record PayPalTransaction(
    string? TransactionId,
    string? InvoiceId,
    string? CustomField,
    string? Status,
    decimal? Amount,
    string? Currency,
    string? EventCode,
    DateTimeOffset? InitiationDate);

/// <summary>
/// The result of a reconciliation transaction search over a range. <see cref="Complete"/> is
/// false if any window/page could not be fully retrieved, so a truncated result is visible to the
/// caller rather than looking complete.
/// </summary>
public record PayPalReconciliationResult(
    IReadOnlyList<PayPalTransaction> Transactions,
    bool Complete,
    int WindowsCovered,
    int PagesRetrieved);
