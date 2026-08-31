using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>One event in how a bill reached its current state, as the provider reports it.</summary>
public record InvoiceHistoryResponse(string Event, DateTimeOffset? Date);

/// <summary>
/// Full view of a single bill. <c>invoiceId</c> is the identifier the operator endpoints act on;
/// <c>paymentLink</c> is present only once the bill has been put to the shopper.
/// </summary>
public class InvoiceResponse
{
    public required string InvoiceId { get; init; }
    public required int OrderId { get; init; }
    public required string State { get; init; }
    public string? ProviderStatus { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required DateOnly DueDate { get; init; }
    public string? CustomerName { get; init; }
    public string? CustomerEmail { get; init; }
    public string? PaymentLink { get; init; }
    public IReadOnlyList<InvoiceHistoryResponse> History { get; init; } = Array.Empty<InvoiceHistoryResponse>();
}

/// <summary>An entry in the caller's list of their own bills. Carries its own <c>invoiceId</c>.</summary>
public class MyInvoiceResponse
{
    public required string InvoiceId { get; init; }
    public required int OrderId { get; init; }
    public required string State { get; init; }
    public string? ProviderStatus { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required DateOnly DueDate { get; init; }
}

/// <summary>One line of the reconciliation report. Carries its own <c>invoiceId</c>.</summary>
public class ReconciliationEntryResponse
{
    public required string InvoiceId { get; init; }
    /// <summary>Matched, ProviderOnly (may not be eShop's), or EShopOnly.</summary>
    public required string Classification { get; init; }
    public bool BearsEShopMarker { get; init; }
    public string? ProviderStatus { get; init; }
    public decimal? Amount { get; init; }
    public string? Currency { get; init; }
    public string? CustomerName { get; init; }
    public DateTimeOffset? RaisedAt { get; init; }
    public int? OrderId { get; init; }
    public string? BuyerId { get; init; }
}

public class ReconciliationSummaryResponse
{
    public int TotalProviderInvoicesInRange { get; init; }
    public int Matched { get; init; }
    public int ProviderOnly { get; init; }
    public int EShopOnly { get; init; }
}

public class ReconciliationResponse
{
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }
    public required ReconciliationSummaryResponse Summary { get; init; }
    public IReadOnlyList<ReconciliationEntryResponse> Entries { get; init; } = Array.Empty<ReconciliationEntryResponse>();
}
