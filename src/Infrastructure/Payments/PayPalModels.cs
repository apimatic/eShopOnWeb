using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed record PayPalBillingAddress(
    string AddressLine1,
    string? AddressLine2,
    string AdminArea2,
    string AdminArea1,
    string PostalCode,
    string CountryCode);

public sealed record PayPalCard(
    string Name,
    string Number,
    string Expiry,
    string SecurityCode,
    PayPalBillingAddress BillingAddress);

public sealed record PayPalOrderResult(string Id, string Status);

public sealed record PayPalAuthorizationResult(
    string Id,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? CreateTime,
    DateTimeOffset? UpdateTime,
    DateTimeOffset? ExpirationTime);

public sealed record PayPalCaptureResult(
    string Id,
    string Status,
    decimal Amount,
    string Currency,
    decimal? Fee,
    decimal? Net,
    DateTimeOffset? CreateTime);

public sealed record PayPalRefundResult(
    string Id,
    string Status,
    decimal Amount,
    DateTimeOffset? CreateTime);

public sealed record PayPalPaymentTokenResult(
    string Id,
    string Brand,
    string LastDigits,
    string Expiry);

public sealed record PayPalTransaction(
    string TransactionId,
    string? PayPalReferenceId,
    string? EventCode,
    string? Status,
    DateTimeOffset? InitiatedAt,
    DateTimeOffset? UpdatedAt,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    string? InvoiceId,
    string? CustomField);

public sealed record PayPalTransactionSearchResult(
    IReadOnlyList<PayPalTransaction> Transactions,
    DateTimeOffset? LastRefreshedAt);
