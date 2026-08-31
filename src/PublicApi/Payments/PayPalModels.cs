using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed record PayPalCard(
    string Name,
    string Number,
    string Expiry,
    string SecurityCode,
    PayPalAddress BillingAddress);

public sealed record PayPalAddress(
    string AddressLine1,
    string? AddressLine2,
    string City,
    string State,
    string PostalCode,
    string CountryCode);

public sealed record PayPalAuthorization(
    string PayPalOrderId,
    string PayPalOrderStatus,
    string AuthorizationId,
    string AuthorizationStatus,
    decimal Amount,
    string Currency,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    string? CardBrand,
    string? CardLastFour);

public sealed record PayPalAuthorizationDetails(
    string Id,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt);

public sealed record PayPalCapture(
    string Id,
    string Status,
    decimal Amount,
    string Currency,
    decimal? PayPalFee,
    decimal? NetAmount,
    DateTimeOffset CreatedAt);

public sealed record PayPalRefund(
    string Id,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset CreatedAt);

public sealed record PayPalSavedCard(
    string VaultId,
    string? CustomerId,
    string Brand,
    string LastFour,
    string Expiry);

public sealed record PayPalTransaction(
    string TransactionId,
    string? PayPalReferenceId,
    string? Status,
    string? EventCode,
    decimal Amount,
    string Currency,
    decimal? Fee,
    string? InvoiceId,
    string? CustomId,
    DateTimeOffset? InitiatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record PayPalTransactionPage(
    IReadOnlyList<PayPalTransaction> Transactions,
    int Page,
    int TotalPages,
    DateTimeOffset? LastRefreshedAt);
