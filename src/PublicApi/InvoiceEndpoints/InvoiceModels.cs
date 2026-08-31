using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// The full state of a bill. <c>invoiceId</c> and <c>paymentLink</c> are top-level so a caller
/// can drive the flow end to end and read back how the bill can be paid.
/// </summary>
public class InvoiceResponse
{
    public required string InvoiceId { get; init; }
    public int OrderId { get; init; }
    public required string Status { get; init; }
    public required string Currency { get; init; }
    public decimal Amount { get; init; }
    public DateOnly DueDate { get; init; }
    public string? CustomerName { get; init; }
    public string? CustomerEmail { get; init; }
    public string? PaymentLink { get; init; }
    public IReadOnlyList<InvoiceHistoryDto> History { get; init; } = Array.Empty<InvoiceHistoryDto>();

    public static InvoiceResponse From(InvoiceView view) => new()
    {
        InvoiceId = view.InvoiceId,
        OrderId = view.OrderId,
        Status = view.Status,
        Currency = view.CurrencyCode,
        Amount = view.Amount,
        DueDate = view.DueDate,
        CustomerName = view.CustomerName,
        CustomerEmail = view.CustomerEmail,
        PaymentLink = view.PaymentLink,
        History = view.History.Select(h => new InvoiceHistoryDto { Event = h.Event, Date = h.Date }).ToList()
    };
}

public class InvoiceHistoryDto
{
    public string? Event { get; init; }
    public DateTimeOffset? Date { get; init; }
}

public class InvoiceSummaryDto
{
    public required string InvoiceId { get; init; }
    public int OrderId { get; init; }
    public required string Status { get; init; }
    public required string Currency { get; init; }
    public decimal Amount { get; init; }
    public DateOnly DueDate { get; init; }
    public DateTimeOffset CreatedAt { get; init; }

    public static InvoiceSummaryDto From(InvoiceSummaryView view) => new()
    {
        InvoiceId = view.InvoiceId,
        OrderId = view.OrderId,
        Status = view.Status,
        Currency = view.CurrencyCode,
        Amount = view.Amount,
        DueDate = view.DueDate,
        CreatedAt = view.CreatedAt
    };
}

public class MyInvoicesResponse
{
    public IReadOnlyList<InvoiceSummaryDto> Invoices { get; init; } = Array.Empty<InvoiceSummaryDto>();
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public int ProviderInvoiceCount { get; init; }
    public int EShopInvoiceCount { get; init; }
    public int MatchedCount { get; init; }
    public int ProviderOnlyCount { get; init; }
    public int EShopOnlyCount { get; init; }
    public IReadOnlyList<ReconciliationEntryDto> Entries { get; init; } = Array.Empty<ReconciliationEntryDto>();

    public static ReconciliationResponse Create(ReconciliationReport report) => new()
    {
        From = report.From,
        To = report.To,
        ProviderInvoiceCount = report.ProviderInvoiceCount,
        EShopInvoiceCount = report.EShopInvoiceCount,
        MatchedCount = report.MatchedCount,
        ProviderOnlyCount = report.ProviderOnlyCount,
        EShopOnlyCount = report.EShopOnlyCount,
        Entries = report.Entries.Select(ReconciliationEntryDto.From).ToList()
    };
}

public class ReconciliationEntryDto
{
    public required string InvoiceId { get; init; }
    public required string Source { get; init; }
    public bool RecognizedByEShop { get; init; }
    public string? ProviderStatus { get; init; }
    public int? OrderId { get; init; }
    public string? BuyerId { get; init; }
    public decimal? Amount { get; init; }
    public string? Currency { get; init; }
    public string? CustomerName { get; init; }
    public DateTimeOffset? ProviderCreatedDate { get; init; }
    public DateTimeOffset? EShopCreatedAt { get; init; }

    public static ReconciliationEntryDto From(ReconciliationEntry entry) => new()
    {
        InvoiceId = entry.InvoiceId,
        Source = entry.Source.ToString(),
        RecognizedByEShop = entry.RecognizedByEShop,
        ProviderStatus = entry.ProviderStatus,
        OrderId = entry.OrderId,
        BuyerId = entry.BuyerId,
        Amount = entry.Amount,
        Currency = entry.CurrencyCode,
        CustomerName = entry.CustomerName,
        ProviderCreatedDate = entry.ProviderCreatedDate,
        EShopCreatedAt = entry.EShopCreatedAt
    };
}
