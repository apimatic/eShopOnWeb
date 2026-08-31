using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the billing capability: raising a bill for an order, reading and correcting it,
/// putting it to the shopper and taking it back, and the operator's reconciliation view. Enforces
/// that a bill belongs to the shopper whose order it was raised against — one shopper never sees or
/// corrects another's — while operator actions may act on any shopper's bill.
/// </summary>
public interface IInvoiceService
{
    /// <summary>Raise a bill for an order the caller owns. What is billed comes from the order itself.</summary>
    Task<OperationResult<InvoiceDetailView>> RaiseForOrderAsync(
        string buyerId, bool isOperator, int orderId, DateOnly dueDate, CustomerDetails? customerOverrides,
        CancellationToken cancellationToken = default);

    /// <summary>Read a bill's current state, provider history and (once issued) how it can be paid.</summary>
    Task<OperationResult<InvoiceDetailView>> GetAsync(
        string buyerId, bool isOperator, string invoiceId, CancellationToken cancellationToken = default);

    /// <summary>Correct the due date or customer details of a bill that has not yet been put to the shopper.</summary>
    Task<OperationResult<InvoiceDetailView>> CorrectAsync(
        string buyerId, bool isOperator, string invoiceId, DateOnly? dueDate, CustomerDetails? customerDetails,
        CancellationToken cancellationToken = default);

    /// <summary>Put a bill to the shopper (operator action).</summary>
    Task<OperationResult<InvoiceDetailView>> IssueAsync(
        string buyerId, bool isOperator, string invoiceId, CancellationToken cancellationToken = default);

    /// <summary>Withdraw a bill so it is no longer payable (operator action).</summary>
    Task<OperationResult<InvoiceDetailView>> WithdrawAsync(
        string buyerId, bool isOperator, string invoiceId, CancellationToken cancellationToken = default);

    /// <summary>The caller's own bills, each showing where it has got to.</summary>
    Task<OperationResult<IReadOnlyList<InvoiceListItemView>>> ListMineAsync(
        string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Reconcile the provider's record of bills raised in a range against eShop's own (operator action).</summary>
    Task<OperationResult<ReconciliationReportView>> ReconcileAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
