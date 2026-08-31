using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates billing: raising a bill against an order with the provider, keeping eShop's own
/// record of it, putting it to the shopper, taking it back, and reporting on what has been billed.
/// Shopper-scoped operations act only on the caller's own data; operator operations act on any bill.
/// </summary>
public interface IInvoiceService
{
    /// <summary>Raise a bill against the caller's order. What is billed comes from the order.</summary>
    Task<Invoice> RaiseInvoiceAsync(int orderId, string buyerId, DateOnly dueDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Read a bill's current state (live from the provider). Shopper callers only see their own bills;
    /// an operator can read any bill, including ones eShop did not raise.
    /// </summary>
    Task<InvoiceDetail> GetInvoiceAsync(string invoiceId, string buyerId, bool isOperator, CancellationToken cancellationToken = default);

    /// <summary>
    /// Correct the due date and/or customer details on the caller's bill, while it has not yet been
    /// put to the shopper. Null arguments leave the corresponding value unchanged.
    /// </summary>
    Task<Invoice> CorrectInvoiceAsync(string invoiceId, string buyerId, DateOnly? dueDate, string? customerName, string? customerEmail, CancellationToken cancellationToken = default);

    /// <summary>Operator action: put the bill to the shopper so a way to pay it can be handed out.</summary>
    Task<InvoiceDetail> IssueInvoiceAsync(string invoiceId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: withdraw a bill that should not be paid.</summary>
    Task<Invoice> WithdrawInvoiceAsync(string invoiceId, CancellationToken cancellationToken = default);

    /// <summary>The caller's own bills, each showing where it has got to.</summary>
    Task<IReadOnlyList<Invoice>> GetMyInvoicesAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: reconcile the provider's record against eShop's over a date range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
