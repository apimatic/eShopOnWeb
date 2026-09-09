using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>
/// Card details for a one-off payment or for vaulting. These flow to PayPal only and are
/// never persisted by the application or written to logs.
/// </summary>
public record PayPalCardDetails(
    string Number,
    string Expiry,          // YYYY-MM
    string SecurityCode,
    string? CardholderName,
    PayPalBillingAddress? BillingAddress);

public record PayPalBillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,     // city
    string? AdminArea1,     // state / province
    string? PostalCode,
    string? CountryCode);

/// <summary>
/// A request to place-and-authorize an order at PayPal. Exactly one of <see cref="Card"/> or
/// <see cref="VaultId"/> is supplied.
/// </summary>
public record AuthorizeOrderRequest(
    string ReferenceId,
    string InvoiceId,
    string CustomId,
    decimal Amount,
    string CurrencyCode,
    string IdempotencyKey,
    PayPalCardDetails? Card,
    string? VaultId);

/// <summary>Result of authorizing an order: the hold PayPal is now placing on the money.</summary>
public record GatewayAuthorization(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt,
    string? CardBrand,
    string? CardLastDigits);

public record GatewayAuthorizationInfo(
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

/// <summary>Result of capturing: what PayPal reported it actually took.</summary>
public record GatewayCapture(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string CurrencyCode);

public record GatewayRefund(
    string RefundId,
    string Status,
    decimal Amount,
    string CurrencyCode);

public record VaultCardRequest(
    PayPalCardDetails Card,
    string MerchantCustomerId,
    string? ExistingCustomerId);

public record GatewayVaultedCard(
    string VaultId,
    string? CustomerId,
    string Brand,
    string LastDigits,
    string Expiry);

/// <summary>One transaction as PayPal's own reporting records it, for reconciliation.</summary>
public record ReconciliationTransaction(
    string TransactionId,
    string? EventCode,
    string Status,
    decimal Amount,
    string CurrencyCode,
    decimal? FeeAmount,
    DateTimeOffset? InitiationDate,
    string? InvoiceId,
    string? CustomField);
