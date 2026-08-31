using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.PayPal;

/// <summary>
/// Full card details, held only in memory for the duration of a single call.
/// Never persisted, never logged.
/// </summary>
public record PayPalCardDetails(
    string Number,
    string Expiry, // PayPal format: YYYY-MM
    string? SecurityCode,
    string? Name,
    PayPalBillingAddress? BillingAddress);

public record PayPalBillingAddress(
    string? AddressLine1,
    string? AdminArea1, // state
    string? AdminArea2, // city
    string? PostalCode,
    string CountryCode);

/// <param name="Authorization">For direct card payments PayPal authorizes inline at
/// order creation; this carries the resulting authorization. Null when a separate
/// authorize call is required.</param>
public record PayPalOrderCreated(string Id, string Status, string? CardBrand, string? CardLastDigits,
    PayPalAuthorizationInfo? Authorization);

public record PayPalAuthorizationInfo(
    string Id,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? ExpirationTime);

public record PayPalCaptureInfo(
    string Id,
    string Status,
    decimal GrossAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string Currency);

public record PayPalRefundInfo(string Id, string Status, decimal? Amount, string Currency);

public record PayPalSetupTokenInfo(string Id, string Status, string? CustomerId);

public record PayPalPaymentTokenInfo(
    string Id,
    string? CustomerId,
    string? Brand,
    string? LastDigits,
    string? Expiry);

public record PayPalTransactionInfo(
    string TransactionId,
    string? EventCode,
    string? Status,
    decimal? Amount,
    string? Currency,
    DateTimeOffset? Time);
