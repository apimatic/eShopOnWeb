using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Full card details used only in transit to PayPal. Never persisted, never logged.
/// </summary>
public sealed record CardPaymentSource(
    string Number,
    string Expiry,
    string? SecurityCode,
    string? Name,
    CardBillingAddress? BillingAddress);

public sealed record CardBillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    string CountryCode);

public sealed record PayPalOrderResult(string Id, string Status);

public sealed record PayPalAuthorizeResult(
    string OrderId,
    string OrderStatus,
    string? AuthorizationId,
    string? AuthorizationStatus,
    decimal? Amount,
    string? Currency,
    DateTimeOffset? ExpirationTime,
    bool RequiresBuyerAction,
    string? BuyerActionUrl,
    string? CardBrand,
    string? CardLastDigits);

public sealed record PayPalAuthorizationDetails(
    string Id,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? ExpirationTime);

public sealed record PayPalCaptureResult(
    string Id,
    string Status,
    decimal Amount,
    string Currency,
    decimal? PayPalFee,
    decimal? NetAmount);

public sealed record PayPalRefundResult(
    string Id,
    string Status,
    decimal Amount,
    string Currency);

public sealed record PayPalSetupTokenResult(
    string Id,
    string Status,
    bool RequiresBuyerAction,
    string? BuyerActionUrl);

public sealed record PayPalPaymentTokenResult(
    string Id,
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? CardholderName);

public sealed record PayPalTransactionRecord(
    string TransactionId,
    string? EventCode,
    string? Status,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    string? InvoiceId,
    string? CustomField,
    string? ReferenceId,
    string? ReferenceIdType,
    DateTimeOffset? InitiationTime,
    DateTimeOffset? UpdatedTime);
