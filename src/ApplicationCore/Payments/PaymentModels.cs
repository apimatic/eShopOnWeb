using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed class CardDetails
{
    public string Number { get; init; } = null!;
    public string Expiry { get; init; } = null!;
    public string SecurityCode { get; init; } = null!;
    public string Name { get; init; } = null!;
    public CardBillingAddress BillingAddress { get; init; } = null!;
}

public sealed class CardBillingAddress
{
    public string AddressLine1 { get; init; } = null!;
    public string? AddressLine2 { get; init; }
    public string City { get; init; } = null!;
    public string State { get; init; } = null!;
    public string PostalCode { get; init; } = null!;
    public string CountryCode { get; init; } = null!;
}

public sealed record OrderLineInput(int CatalogItemId, int Quantity);
public sealed record PayPalOrderResult(string Id, string Status);
public sealed record PayPalAuthorizationResult(string Id, string Status, decimal Amount, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt);
public sealed record PayPalCaptureResult(string Id, string Status, decimal Amount, decimal? PayPalFee, decimal? NetAmount, DateTimeOffset CreatedAt);
public sealed record PayPalRefundResult(string Id, string Status, decimal Amount, DateTimeOffset CreatedAt);
public sealed record PayPalVaultResult(string TokenId, string CustomerId, string Brand, string Last4, string Expiry);

public sealed record PayPalTransaction(
    string TransactionId,
    string? ReferenceId,
    string? InvoiceId,
    string Status,
    string EventCode,
    decimal? Amount,
    string? Currency,
    DateTimeOffset? InitiationDate,
    DateTimeOffset? UpdatedDate);

public sealed record ReconciliationItem(
    string Source,
    string MatchStatus,
    int? OrderId,
    string? PayPalTransactionId,
    string? PayPalOrderId,
    string? CaptureId,
    string Status,
    decimal? Amount,
    string? Currency,
    DateTimeOffset? TransactionDate);

public sealed record ReconciliationReport(DateTimeOffset From, DateTimeOffset To, IReadOnlyList<ReconciliationItem> Items);
