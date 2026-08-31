using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>
/// What the provider reports about a single invoice: its identifier there, where it currently
/// stands, how it can be paid (once it has been put to the shopper), and the trail of events
/// showing how it reached that state.
/// </summary>
public class ProviderInvoiceResult
{
    public ProviderInvoiceResult(string id,
        string status,
        string? paymentLink,
        DateOnly? dueDate,
        decimal? amount,
        string? currencyCode,
        string? customerName,
        string? customerEmail,
        IReadOnlyList<ProviderInvoiceEvent> history)
    {
        Id = id;
        Status = status;
        PaymentLink = paymentLink;
        DueDate = dueDate;
        Amount = amount;
        CurrencyCode = currencyCode;
        CustomerName = customerName;
        CustomerEmail = customerEmail;
        History = history;
    }

    public string Id { get; }
    public string Status { get; }
    public string? PaymentLink { get; }
    public DateOnly? DueDate { get; }
    public decimal? Amount { get; }
    public string? CurrencyCode { get; }
    public string? CustomerName { get; }
    public string? CustomerEmail { get; }
    public IReadOnlyList<ProviderInvoiceEvent> History { get; }
}

public class ProviderInvoiceEvent
{
    public ProviderInvoiceEvent(string? name, DateTimeOffset? date)
    {
        Name = name;
        Date = date;
    }

    public string? Name { get; }
    public DateTimeOffset? Date { get; }
}

/// <summary>
/// A lighter view of a provider invoice used when listing the account's invoices for reconciliation.
/// </summary>
public class ProviderInvoiceSummary
{
    public ProviderInvoiceSummary(string id,
        string status,
        DateTimeOffset? createdDate,
        decimal? amount,
        string? currencyCode,
        string? customerName)
    {
        Id = id;
        Status = status;
        CreatedDate = createdDate;
        Amount = amount;
        CurrencyCode = currencyCode;
        CustomerName = customerName;
    }

    public string Id { get; }
    public string Status { get; }
    public DateTimeOffset? CreatedDate { get; }
    public decimal? Amount { get; }
    public string? CurrencyCode { get; }
    public string? CustomerName { get; }
}
