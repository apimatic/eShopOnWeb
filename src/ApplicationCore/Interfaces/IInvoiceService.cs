using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The application service for customer invoicing. It coordinates the eShop order/invoice records with the
/// invoicing provider and enforces the rules the task states: what is billed comes from the order; a bill
/// belongs to the shopper whose order it was; operators may act on any shopper's bill.
/// </summary>
public interface IInvoiceService
{
    /// <summary>
    /// Raise a bill with the provider for an order, held as a draft. What is billed is taken from the order
    /// (its items and their cost), not from the caller. Only the order's own buyer may raise its bill.
    /// </summary>
    Task<Invoice> RaiseInvoiceAsync(
        int orderId,
        string callerId,
        DateTimeOffset dueDate,
        string? customerName,
        string? customerEmail,
        CancellationToken cancellationToken);

    /// <summary>
    /// The bill's current state, refreshed from the provider (status, how it got there, and — once put to
    /// the shopper — how to pay it). A shopper may only read their own bill; an operator may read any.
    /// </summary>
    Task<InvoiceDetails> GetInvoiceAsync(int invoiceId, string callerId, bool isOperator, CancellationToken cancellationToken);

    /// <summary>
    /// Correct the due date and/or customer details of a bill that has not yet been put to the shopper. The
    /// amount is not correctable. Refused once the bill is issued or withdrawn.
    /// </summary>
    Task<InvoiceDetails> ReviseInvoiceAsync(
        int invoiceId,
        string callerId,
        bool isOperator,
        DateTimeOffset? dueDate,
        string? customerName,
        string? customerEmail,
        bool customerNameProvided,
        bool customerEmailProvided,
        CancellationToken cancellationToken);

    /// <summary>Put the bill to the shopper (operator action). Afterwards a payment link can be handed out.</summary>
    Task<InvoiceDetails> IssueInvoiceAsync(int invoiceId, CancellationToken cancellationToken);

    /// <summary>Withdraw the bill (operator action). Afterwards it is no longer payable.</summary>
    Task<InvoiceDetails> WithdrawInvoiceAsync(int invoiceId, CancellationToken cancellationToken);

    /// <summary>The caller's own bills, each showing where it has got to.</summary>
    Task<System.Collections.Generic.IReadOnlyList<Invoice>> GetMyInvoicesAsync(string callerId, CancellationToken cancellationToken);

    /// <summary>The operator's reconciliation report over a date range (operator action).</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
