using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed record BillingAddressData(
    string AddressLine1,
    string? AddressLine2,
    string City,
    string State,
    string PostalCode,
    string CountryCode);

public sealed record CardData(
    string Number,
    string Expiry,
    string SecurityCode,
    string Name,
    BillingAddressData BillingAddress);

public sealed record OrderLineData(int CatalogItemId, int Quantity);

public sealed record PayPalAuthorization(
    string PayPalOrderId,
    string PayPalOrderStatus,
    string AuthorizationId,
    string AuthorizationStatus,
    decimal Amount,
    string Currency,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

public sealed record PayPalCapture(
    string Id,
    string Status,
    decimal Amount,
    string Currency,
    decimal Fee,
    decimal NetAmount);

public sealed record PayPalRefund(
    string Id,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset CreatedAt);

public sealed record PayPalVaultToken(
    string Id,
    string CustomerId,
    string Brand,
    string LastDigits,
    string Expiry);

public sealed record PayPalTransaction(
    string TransactionId,
    string? ReferenceId,
    string? ReferenceIdType,
    string EventCode,
    string Status,
    DateTimeOffset InitiatedAt,
    decimal Amount,
    decimal Fee,
    string Currency);

public sealed record ReconciliationEntry(
    string Source,
    string? PayPalTransactionId,
    int? OrderId,
    string? PayPalOrderId,
    string? CaptureId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset OccurredAt,
    bool IsMatched);

public sealed record ReconciliationResult(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyCollection<ReconciliationEntry> Entries);
