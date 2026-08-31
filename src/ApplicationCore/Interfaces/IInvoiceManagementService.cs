using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the invoicing flows: raising a bill against an order with the provider, keeping eShop's
/// local record in step with it, and enforcing that a shopper only ever touches their own bills while an
/// operator may act on any. Amounts always come from the order, never from a caller.
/// </summary>
public interface IInvoiceManagementService
{
    /// <summary>
    /// Raise a bill with the provider for the given order and record it locally. The bill starts out
    /// as a draft, not yet put to the shopper. Throws if the order is not visible to the caller.
    /// </summary>
    Task<InvoiceSnapshot> RaiseInvoiceForOrderAsync(
        int orderId,
        DateOnly dueDate,
        VisaCustomer? customer,
        string callerId,
        bool isOperator,
        CancellationToken cancellationToken = default);

    /// <summary>Read a bill (local record plus live provider state), scoped to the caller unless they are an operator.</summary>
    Task<InvoiceSnapshot> GetInvoiceAsync(string invoiceId, string callerId, bool isOperator, CancellationToken cancellationToken = default);

    /// <summary>
    /// Correct the due date and/or customer details of a bill that has not yet been put to the shopper.
    /// The amount is not correctable — it is re-derived from the order. Throws if the bill is already
    /// issued or withdrawn.
    /// </summary>
    Task<InvoiceSnapshot> CorrectInvoiceAsync(
        string invoiceId,
        DateOnly? dueDate,
        VisaCustomer? customer,
        string callerId,
        bool isOperator,
        CancellationToken cancellationToken = default);

    /// <summary>Put the bill to the shopper (operator action).</summary>
    Task<InvoiceSnapshot> IssueInvoiceAsync(string invoiceId, CancellationToken cancellationToken = default);

    /// <summary>Withdraw the bill so it can no longer be paid (operator action).</summary>
    Task<InvoiceSnapshot> WithdrawInvoiceAsync(string invoiceId, CancellationToken cancellationToken = default);

    /// <summary>The caller's own bills, each with its last-known status.</summary>
    Task<IReadOnlyList<Invoice>> GetInvoicesForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Reconcile the provider's ledger against eShop's records over a date range (operator action).</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
