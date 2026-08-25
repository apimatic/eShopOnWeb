using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

public record PaymentAmount(decimal Value, string CurrencyCode);

public record BillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea1,
    string? AdminArea2,
    string? PostalCode,
    string CountryCode);

public record CardDetails(
    string Number,
    string Expiry,
    string SecurityCode,
    string? CardholderName,
    BillingAddress? BillingAddress);

/// <summary>Result of creating a hold on funds (an authorization).</summary>
public record PaymentAuthorizationResult(
    string PayPalOrderId,
    string? AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt,
    bool RequiresShopperAction);

public record PaymentAuthorizationStatusResult(
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

public record PaymentCaptureResult(
    string CaptureId,
    string Status,
    decimal CapturedAmount,
    decimal? FeeAmount,
    decimal? NetAmount);

public record PaymentRefundResult(
    string RefundId,
    string Status,
    decimal Amount);

public record VaultedCardResult(
    string VaultId,
    string? CardBrand,
    string? Last4,
    string? Expiry,
    string? CardholderName);

public record PayPalTransaction(
    string TransactionId,
    string? Status,
    decimal? Amount,
    string? CurrencyCode,
    DateTimeOffset? InitiatedAt,
    string? ReferenceId,
    string? ReferenceIdType);

public record TransactionSearchResult(IReadOnlyList<PayPalTransaction> Transactions);
