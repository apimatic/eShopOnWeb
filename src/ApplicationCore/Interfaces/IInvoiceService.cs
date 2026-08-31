using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the customer-invoicing use cases: raising a bill against an order, reading and
/// correcting it, putting it to the shopper, withdrawing it, and reconciling eShop's records against
/// the provider. Ownership is enforced here — a shopper only ever sees or acts on their own bills;
/// <paramref name="isAdmin"/> lifts that scoping for operator callers.
/// </summary>
public interface IInvoiceService
{
    /// <summary>Raise a bill with the provider for the given order. Returns the provider invoice id and status.</summary>
    Task<RaisedInvoice> RaiseInvoiceAsync(
        string buyerId, bool isAdmin, int orderId, DateTime dueDate,
        string customerName, string customerEmail, CancellationToken cancellationToken = default);

    Task<InvoiceDetails> GetInvoiceAsync(
        string buyerId, bool isAdmin, string invoiceId, CancellationToken cancellationToken = default);

    /// <summary>Correct the due date and/or customer details of a bill still in DRAFT.</summary>
    Task<InvoiceDetails> CorrectInvoiceAsync(
        string buyerId, bool isAdmin, string invoiceId,
        DateTime? dueDate, string? customerName, string? customerEmail,
        CancellationToken cancellationToken = default);

    /// <summary>Operator action: put the bill to the shopper.</summary>
    Task<InvoiceDetails> IssueInvoiceAsync(string invoiceId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: withdraw the bill.</summary>
    Task<InvoiceDetails> WithdrawInvoiceAsync(string invoiceId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InvoiceSummaryView>> GetInvoicesForShopperAsync(
        string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: reconcile the provider's bills against eShop's over a date range.</summary>
    Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
