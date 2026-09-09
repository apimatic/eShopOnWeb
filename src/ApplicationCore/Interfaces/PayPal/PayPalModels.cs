using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

/// <summary>
/// A portable billing address for a card, matching the fields PayPal's card schema expects.
/// </summary>
public record PayPalBillingAddress(
    string AddressLine1,
    string? AddressLine2,
    string? AdminArea2,
    string? AdminArea1,
    string? PostalCode,
    string CountryCode);

/// <summary>
/// Raw card details for a one-off card payment or for vaulting. These are passed straight to
/// PayPal and are never persisted or logged by this application.
/// </summary>
public record PayPalCardInput(
    string Number,
    string Expiry,
    string SecurityCode,
    string? Name,
    PayPalBillingAddress? BillingAddress);

/// <summary>Result of authorizing (holding) an order total.</summary>
public record PayPalAuthorizationResult(
    string PayPalOrderId,
    string OrderStatus,
    string? AuthorizationId,
    string? AuthorizationStatus,
    string? CardBrand,
    string? CardLastFour,
    bool RequiresPayerAction);

/// <summary>Result of capturing a hold, including what PayPal reported.</summary>
public record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal Amount,
    decimal? PayPalFee,
    decimal? NetAmount);

/// <summary>Result of reauthorizing a stale hold.</summary>
public record PayPalReauthorizeResult(
    string AuthorizationId,
    string Status);

/// <summary>Result of refunding a capture, in full or in part.</summary>
public record PayPalRefundResult(
    string RefundId,
    string Status,
    decimal Amount);

/// <summary>Result of vaulting a card, describing it safely for later recognition.</summary>
public record PayPalVaultCardResult(
    string VaultId,
    string? CustomerId,
    string CardBrand,
    string LastFour,
    string Expiry,
    string? CardHolderName);

/// <summary>
/// One transaction from PayPal's own reporting record, used to line PayPal's ledger up against
/// eShop orders during reconciliation.
/// </summary>
public record PayPalTransactionRecord(
    string TransactionId,
    string? InvoiceId,
    string? CustomField,
    string? Status,
    string? EventCode,
    decimal? Amount,
    string? CurrencyCode,
    decimal? FeeAmount,
    DateTimeOffset? InitiationDate);
