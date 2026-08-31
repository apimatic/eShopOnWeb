using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates eShop's invoicing: it drives the provider and keeps eShop's own record of each
/// bill in step, while enforcing that a shopper only ever sees or corrects their own bills and
/// that operator-only actions are reserved for operators.
/// </summary>
public interface IInvoiceService
{
    /// <summary>
    /// Raises a bill with the provider for the given order and records it. What is billed comes
    /// from the order itself. The bill starts out not yet put to the shopper.
    /// </summary>
    Task<InvoiceView> RaiseInvoiceForOrderAsync(int orderId, string buyerId, bool isOperator, DateOnly dueDate, CancellationToken cancellationToken = default);

    /// <summary>Reads a single bill, refreshing its state from the provider.</summary>
    Task<InvoiceView> GetInvoiceAsync(string invoiceId, string callerBuyerId, bool isOperator, CancellationToken cancellationToken = default);

    /// <summary>
    /// Corrects the due date and/or customer details a bill carries, while it is still a draft.
    /// Null arguments leave the corresponding value unchanged.
    /// </summary>
    Task<InvoiceView> CorrectInvoiceAsync(string invoiceId, string callerBuyerId, bool isOperator,
        DateOnly? dueDate, string? customerName, string? customerEmail, CancellationToken cancellationToken = default);

    /// <summary>Puts the bill to the shopper (operator action).</summary>
    Task<InvoiceView> IssueInvoiceAsync(string invoiceId, CancellationToken cancellationToken = default);

    /// <summary>Withdraws the bill so it is no longer payable (operator action).</summary>
    Task<InvoiceView> WithdrawInvoiceAsync(string invoiceId, CancellationToken cancellationToken = default);

    /// <summary>Lists the caller's own bills, each showing where it has got to.</summary>
    Task<IReadOnlyList<InvoiceSummaryView>> GetInvoicesForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Reconciles the provider's record of bills against eShop's over a date range (operator action).</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
