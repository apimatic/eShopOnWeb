using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentGateway;

/// <summary>Raw card details for a one-off payment or for vaulting. Never persisted by this app.</summary>
public record CardDetails(
    string Number,
    string Expiry, // "YYYY-MM"
    string SecurityCode,
    string? CardholderName,
    BillingAddress? BillingAddress);

/// <summary>Billing address supplied with a card (used for AVS; optional).</summary>
public record BillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2, // city
    string? AdminArea1, // state/province
    string? PostalCode,
    string CountryCode);

/// <summary>What to authorize, and how to pay for it (exactly one of <see cref="Card"/> / <see cref="VaultId"/>).</summary>
public record AuthorizeRequest(
    decimal Amount,
    string CurrencyCode,
    string MerchantReference,
    string BuyerReference,
    string CreateRequestId,
    string AuthorizeRequestId,
    CardDetails? Card,
    string? VaultId);

/// <summary>The hold PayPal placed.</summary>
public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string? AuthorizationStatus,
    DateTimeOffset? ExpiresAt);

/// <summary>What PayPal reported when the hold was captured.</summary>
public record CaptureResult(
    string CaptureId,
    string? Status,
    decimal GrossAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string CurrencyCode);

/// <summary>The outcome of a refund against a capture.</summary>
public record RefundResult(
    string RefundId,
    string? Status,
    decimal Amount,
    string CurrencyCode);

/// <summary>A vaulted (saved) card, described safely for the shopper — never full card details.</summary>
public record VaultedCardResult(
    string VaultId,
    string? PayPalCustomerId,
    string? Brand,
    string? Last4,
    string? Expiry);

/// <summary>One transaction from PayPal's own record, for reconciliation.</summary>
public record ReconciliationTransaction(
    string TransactionId,
    string? Status,
    decimal? Amount,
    string? CurrencyCode,
    string? InvoiceId,
    DateTimeOffset? InitiationDate,
    DateTimeOffset? UpdatedDate);
