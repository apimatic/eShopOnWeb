using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Models.PayPal;

/// <summary>Raw card details used only in transit to PayPal; never persisted or logged.</summary>
public sealed record PayPalCardDetails(
    string Number,
    string Expiry,
    string? SecurityCode,
    string? CardholderName,
    PayPalBillingAddress? BillingAddress);

public sealed record PayPalBillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,
    string? AdminArea1,
    string? PostalCode,
    string CountryCode);

public sealed record PayPalAuthorizationResult(
    string Id,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? ExpirationTime,
    bool RequiresPayerAction);

public sealed record PayPalCaptureResult(
    string Id,
    string Status,
    decimal Amount,
    string Currency,
    decimal? FeeAmount,
    decimal? NetAmount,
    DateTimeOffset? CreateTime);

public sealed record PayPalRefundResult(
    string Id,
    string Status,
    decimal Amount,
    string Currency);

public sealed record PayPalOrderDetails(
    string Id,
    string Status,
    IReadOnlyList<PayPalAuthorizationResult> Authorizations,
    IReadOnlyList<PayPalCaptureResult> Captures);

public sealed record PayPalVaultedCard(
    string PaymentTokenId,
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
    decimal? FeeAmount,
    DateTimeOffset? InitiatedAt,
    string? InvoiceId,
    string? ReferenceId);
