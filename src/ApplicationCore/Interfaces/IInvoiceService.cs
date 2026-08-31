using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the customer-invoicing capability: it turns orders into bills held with the provider,
/// scopes each bill to its owning shopper, drives the issue/withdraw lifecycle, and reconciles the
/// provider's record against eShop's. Shopper-scoped methods take the caller's buyer id and refuse to
/// act on another shopper's data; operator methods act on any shopper's bill.
/// </summary>
public interface IInvoiceService
{
    /// <summary>Raise a bill with the provider for one of the caller's own orders. Starts as a draft.</summary>
    Task<Invoice> RaiseInvoiceAsync(int orderId, string buyerId, DateOnly dueDate, CancellationToken cancellationToken = default);

    /// <summary>Read one of the caller's own bills, joined with the provider's current view.</summary>
    Task<InvoiceDetailView> GetInvoiceAsync(int invoiceId, string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Correct the due date and/or customer details on one of the caller's own draft bills.</summary>
    Task<Invoice> CorrectInvoiceAsync(
        int invoiceId,
        string buyerId,
        DateOnly? dueDate,
        string? customerName,
        string? customerEmail,
        CancellationToken cancellationToken = default);

    /// <summary>List all of the caller's own bills, each showing where it has got to.</summary>
    Task<IReadOnlyList<Invoice>> GetMyInvoicesAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: put any bill to its shopper so it can be paid.</summary>
    Task<Invoice> IssueInvoiceAsync(int invoiceId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: withdraw any bill so it can no longer be paid.</summary>
    Task<Invoice> WithdrawInvoiceAsync(int invoiceId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: reconcile the provider's record of raised bills against eShop's, over a range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
