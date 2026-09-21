using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>Raw card details for a one-off payment or to vault. Never stored or logged by the app.</summary>
public record CardDetails(
    string Number,
    string Expiry,           // ISO-8601 "YYYY-MM"
    string SecurityCode,
    string? CardholderName,
    CardBillingAddress? BillingAddress);

/// <summary>Optional card billing address (PayPal portable address shape).</summary>
public record CardBillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,      // city
    string? AdminArea1,      // state / province
    string? PostalCode,
    string? CountryCode);    // two-letter ISO-3166-1

public record GatewayAuthorization(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt,
    string? PaymentMethodDescription);

public record GatewayCapture(
    string CaptureId,
    string Status,
    decimal CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount);

public record GatewayReauthorization(
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

public record GatewayRefund(
    string RefundId,
    string Status,
    decimal Amount);

public record GatewaySavedCard(
    string VaultTokenId,
    string Brand,
    string LastFourDigits,
    string? Expiry);

/// <summary>One transaction as PayPal's reporting system records it.</summary>
public record GatewayTransaction(
    string TransactionId,
    string? Status,
    decimal? Amount,
    string? CurrencyCode,
    DateTimeOffset? InitiationDate,
    string? InvoiceId,
    string? CustomField);
