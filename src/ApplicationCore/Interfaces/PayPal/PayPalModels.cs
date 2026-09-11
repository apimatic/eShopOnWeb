using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

/// <summary>Raw card details supplied for a one-off payment or to be vaulted. Never persisted.</summary>
public record CardDetails(
    string Number,
    string ExpiryMonthYear,
    string SecurityCode,
    string? Name,
    PayPalAddress? BillingAddress);

/// <summary>Portable postal address in the shape PayPal expects (address_line_1, admin_area_2, ...).</summary>
public record PayPalAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,
    string? AdminArea1,
    string? PostalCode,
    string? CountryCode);

/// <summary>
/// Input to authorize an order. Exactly one of <see cref="Card"/> or <see cref="VaultId"/>
/// identifies the funding source.
/// </summary>
public record AuthorizeOrderInput(
    decimal Amount,
    string CurrencyCode,
    string InvoiceId,
    string ReferenceId,
    string CustomId,
    CardDetails? Card,
    string? VaultId);

/// <summary>Result of authorizing (placing a hold) via PayPal.</summary>
public record PayPalAuthorization(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt,
    decimal Amount,
    string CurrencyCode);

/// <summary>Result of capturing (taking) an authorization.</summary>
public record PayPalCapture(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal PayPalFee,
    decimal NetAmount,
    string CurrencyCode);

/// <summary>Result of refunding a capture.</summary>
public record PayPalRefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string CurrencyCode);

/// <summary>A card vaulted with PayPal, described safely for display.</summary>
public record VaultedCard(
    string TokenId,
    string? CustomerId,
    string Brand,
    string LastDigits,
    string ExpiryMonthYear,
    string? Name);

/// <summary>A transaction as PayPal's reporting API knows it, for reconciliation.</summary>
public record PayPalTransaction(
    string TransactionId,
    string? InvoiceId,
    string? CustomField,
    decimal Amount,
    string CurrencyCode,
    string Status,
    string EventCode,
    DateTimeOffset InitiationDate,
    decimal? FeeAmount);
