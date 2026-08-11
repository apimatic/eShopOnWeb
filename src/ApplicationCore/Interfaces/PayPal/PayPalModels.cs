using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

/// <summary>A postal address for a card, as PayPal expects it. Only the country code is required.</summary>
public record PayPalAddress(
    string CountryCode,
    string? AddressLine1 = null,
    string? AddressLine2 = null,
    string? AdminArea1 = null,
    string? AdminArea2 = null,
    string? PostalCode = null);

/// <summary>
/// Raw card details for a one-off payment or for vaulting. Never persisted by the application and never
/// written to logs.
/// </summary>
public record PayPalCardDetails(
    string Number,
    string Expiry,
    string SecurityCode,
    string? Name,
    PayPalAddress? BillingAddress);

/// <summary>The outcome of authorizing an order: the hold placed on the shopper's funds.</summary>
public record PayPalAuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt,
    string? CardBrand,
    string? CardLast4,
    bool RequiresBuyerApproval);

/// <summary>The outcome of capturing an authorization, including what PayPal reported.</summary>
public record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string CurrencyCode);

/// <summary>The outcome of refunding a capture.</summary>
public record PayPalRefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string CurrencyCode);

/// <summary>A card saved (vaulted) with PayPal. Carries only safe descriptors plus the vault token.</summary>
public record PayPalVaultedCard(
    string VaultId,
    string CustomerId,
    string? Brand,
    string? Last4,
    string? Expiry,
    string? CardholderName);

/// <summary>One transaction as PayPal's transaction reporting records it, for reconciliation.</summary>
public record PayPalTransaction(
    string TransactionId,
    string? EventCode,
    string? Status,
    decimal? Amount,
    string? CurrencyCode,
    string? InvoiceId,
    string? CustomField,
    DateTimeOffset? InitiationDate);
