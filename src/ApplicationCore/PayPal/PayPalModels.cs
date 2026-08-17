using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.PayPal;

/// <summary>
/// Raw card details supplied for a one-off payment or to be vaulted. This type only ever
/// travels from the API surface to the PayPal client; it is never persisted or logged.
/// </summary>
public record CardDetails(
    string Number,
    string Expiry,          // "YYYY-MM"
    string SecurityCode,
    string? Name,
    CardBillingAddress? BillingAddress);

public record CardBillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,     // city
    string? AdminArea1,     // state
    string? PostalCode,
    string? CountryCode);

/// <summary>Result of authorizing (placing a hold) against PayPal.</summary>
public record PayPalAuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status);

/// <summary>What PayPal reported when the capture was taken.</summary>
public record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal PayPalFee,
    decimal NetAmount,
    string Currency);

public record PayPalRefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

/// <summary>A card stored in PayPal's vault, described safely for the shopper.</summary>
public record VaultedCard(
    string VaultId,
    string? CustomerId,
    string CardBrand,
    string LastFourDigits,
    string Expiry,
    string? CardholderName);

/// <summary>Outcome of a fulfilment capture, distinguishing a renewed (reauthorized) hold.</summary>
public record PayPalCaptureOutcome(
    PayPalCaptureResult Capture,
    bool AuthorizationWasRenewed,
    string? RenewedAuthorizationId,
    string? RenewedAuthorizationStatus);

/// <summary>One transaction as PayPal's reporting knows it.</summary>
public record PayPalTransaction(
    string TransactionId,
    string? ReferenceId,
    string? InvoiceId,
    string? Status,
    string? EventCode,
    decimal Amount,
    decimal Fee,
    string? Currency,
    DateTimeOffset? InitiationDate);
