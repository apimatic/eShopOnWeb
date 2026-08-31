using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the external invoicing provider (Visa via CyberSource). Every method is a single
/// provider interaction; the application service composes them. Implementations translate provider
/// refusals into <see cref="Exceptions.InvoiceProviderException"/> and never leak credentials.
/// </summary>
public interface IInvoiceProvider
{
    /// <summary>Raise a new bill with the provider. It starts out not yet put to the shopper.</summary>
    Task<ProviderInvoice> RaiseAsync(RaiseInvoiceRequest request, CancellationToken cancellationToken = default);

    /// <summary>Ask the provider for the current state of a bill it holds.</summary>
    Task<ProviderInvoice> GetAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>Correct the due date and customer details of a bill not yet put to the shopper.</summary>
    Task<ProviderInvoice> UpdateAsync(string providerInvoiceId, UpdateInvoiceRequest request, CancellationToken cancellationToken = default);

    /// <summary>Put the bill to the shopper (the provider sends/delivers it).</summary>
    Task<ProviderInvoice> IssueAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>Withdraw the bill so it is no longer payable.</summary>
    Task<ProviderInvoice> WithdrawAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The provider's own record of every bill it created in the range, across the whole range.
    /// This includes bills that are not eShop's, so callers must reconcile against eShop's records.
    /// </summary>
    Task<IReadOnlyList<ProviderInvoiceSummary>> ListCreatedBetweenAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>A single billed line, taken from the order.</summary>
public sealed record ProviderLineItem(string ProductName, string Sku, int Quantity, decimal UnitPrice);

/// <summary>What eShop asks the provider to bill. All facts derive from the order.</summary>
public sealed record RaiseInvoiceRequest(
    string InvoiceReference,
    string Description,
    decimal Amount,
    string Currency,
    DateOnly DueDate,
    string CustomerName,
    string CustomerEmail,
    IReadOnlyList<ProviderLineItem> LineItems);

/// <summary>The correctable facts of a bill. The billed amount/line items are resent unchanged from the order.</summary>
public sealed record UpdateInvoiceRequest(
    string Description,
    decimal Amount,
    string Currency,
    DateOnly DueDate,
    string CustomerName,
    string CustomerEmail,
    IReadOnlyList<ProviderLineItem> LineItems);

/// <summary>One event the provider records in a bill's history.</summary>
public sealed record ProviderInvoiceEvent(string Event, DateTimeOffset? Date);

/// <summary>The full state the provider reports for a bill.</summary>
public sealed record ProviderInvoice(
    string Id,
    string? Status,
    string? PaymentLink,
    DateTimeOffset? CreatedDate,
    IReadOnlyList<ProviderInvoiceEvent> History);

/// <summary>A compact provider record used when listing/reconciling.</summary>
public sealed record ProviderInvoiceSummary(
    string Id,
    string? Status,
    DateTimeOffset? CreatedDate,
    decimal? Amount,
    string? Currency,
    string? CustomerName);
