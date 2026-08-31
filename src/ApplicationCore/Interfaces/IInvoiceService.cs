using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates billing a shopper for an order through the Visa invoicing provider,
/// and keeps eShop's local record of each bill in step with the provider.
///
/// Shopper-scoped operations take the caller's buyer id and refuse to act on another
/// shopper's bill. Operator operations (<see cref="IssueAsync"/>, <see cref="WithdrawAsync"/>,
/// <see cref="ReconcileAsync"/>) act on any shopper's bill.
/// </summary>
public interface IInvoiceService
{
    /// <summary>
    /// Raises a bill with the provider for one of the caller's orders. What is billed
    /// comes from the order itself; the caller supplies only the due date. The bill
    /// starts out not yet put to the shopper.
    /// </summary>
    Task<Result<InvoiceDetailView>> RaiseInvoiceForOrderAsync(int orderId, string buyerId, DateOnly dueDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a bill's current state, merging eShop's record with a fresh read from the
    /// provider. <paramref name="buyerId"/> is required unless the caller is an operator;
    /// a shopper may only read their own bill.
    /// </summary>
    Task<Result<InvoiceDetailView>> GetInvoiceAsync(string invoiceId, string? buyerId, bool isOperator, CancellationToken cancellationToken = default);

    /// <summary>
    /// Corrects the due date and/or the customer details on one of the caller's bills.
    /// Only permitted before the bill has been put to the shopper or withdrawn; the
    /// amount is not correctable here. Null arguments leave that detail unchanged.
    /// </summary>
    Task<Result<InvoiceDetailView>> CorrectInvoiceAsync(string invoiceId, string buyerId, DateOnly? dueDate, string? customerName, string? customerEmail, CancellationToken cancellationToken = default);

    /// <summary>Puts the bill to the shopper. Operator action.</summary>
    Task<Result<InvoiceDetailView>> IssueAsync(string invoiceId, CancellationToken cancellationToken = default);

    /// <summary>Withdraws the bill so it is no longer payable. Operator action.</summary>
    Task<Result<InvoiceDetailView>> WithdrawAsync(string invoiceId, CancellationToken cancellationToken = default);

    /// <summary>The caller's own bills, each showing where it has got to.</summary>
    Task<IReadOnlyList<InvoiceSummaryView>> GetInvoicesForShopperAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The operator reconciliation report over a date range: the provider's record of
    /// bills raised in the range lined up against what eShop believes it raised.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
