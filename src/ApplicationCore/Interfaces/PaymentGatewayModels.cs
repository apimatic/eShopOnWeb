using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Raw card details for a one-off (unsaved) card payment or for vaulting. These never touch this
/// app's database or logs — they flow straight through the gateway to PayPal.
/// </summary>
public record GatewayCard(
    string Number,
    string Expiry,          // ISO-8601 YYYY-MM
    string? SecurityCode,
    string? CardholderName,
    GatewayBillingAddress? BillingAddress);

public record GatewayBillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea1,     // state / province
    string? AdminArea2,     // city
    string? PostalCode,
    string? CountryCode);   // ISO-3166 alpha-2

/// <summary>A request to authorize (hold) an amount, funded either by a raw card or a saved card.</summary>
public record GatewayAuthorizeRequest(
    decimal Amount,
    string CurrencyCode,
    string InvoiceId,
    string CustomId,
    string Description,
    string IdempotencyKey,
    GatewayCard? Card,
    string? VaultId);

public record GatewayAuthorization(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

public record GatewayAuthorizationState(
    string Status,
    DateTimeOffset? ExpiresAt);

public record GatewayCapture(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal? Fee,
    decimal? NetAmount,
    string CurrencyCode);

public record GatewayRefund(
    string RefundId,
    string Status,
    decimal Amount,
    string CurrencyCode);

/// <summary>A request to vault (save) a card for later reuse.</summary>
public record GatewayVaultCardRequest(
    GatewayCard Card,
    string? PayPalCustomerId,
    string MerchantCustomerId);

public record GatewayVaultedCard(
    string VaultId,
    string? CustomerId,
    string? Brand,
    string? Last4,
    string? Expiry,
    string? CardholderName);

/// <summary>One transaction as PayPal's own reporting knows it, for reconciliation.</summary>
public record GatewayTransaction(
    string? TransactionId,
    string? InvoiceId,
    string? Status,
    decimal? Amount,
    string? CurrencyCode,
    decimal? Fee,
    DateTimeOffset? InitiationDate);
