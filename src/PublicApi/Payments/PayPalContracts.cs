using System;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed record PayPalAddress(
    string AddressLine1,
    string? AddressLine2,
    string City,
    string State,
    string PostalCode,
    string CountryCode);

public sealed record PayPalCard(
    string Name,
    string Number,
    string Expiry,
    string SecurityCode,
    PayPalAddress BillingAddress);

public sealed record PayPalOrderResult(string Id, string Status);

public sealed record PayPalAuthorizationResult(
    string Id,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ExpiresAt);

public sealed record PayPalCaptureResult(
    string Id,
    string Status,
    decimal Amount,
    string Currency,
    decimal? Fee,
    decimal? NetAmount,
    DateTimeOffset? CreatedAt);

public sealed record PayPalRefundResult(
    string Id,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? CreatedAt);

public sealed record PayPalVaultResult(
    string Id,
    string? CustomerId,
    string Brand,
    string Last4,
    string Expiry);

public sealed record PayPalTransaction(
    string TransactionId,
    string? ReferenceId,
    string? ReferenceType,
    string? EventCode,
    string? Status,
    DateTimeOffset? InitiatedAt,
    DateTimeOffset? UpdatedAt,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    string? InvoiceId,
    string? CustomField);
