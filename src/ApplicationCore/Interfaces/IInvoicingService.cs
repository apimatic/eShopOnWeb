using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates customer invoicing: raising a bill from an order, correcting, issuing and withdrawing
/// it, listing a shopper's bills, and reconciling the provider's record against eShop's. Ownership
/// scoping (a shopper only ever sees their own bills; an operator sees any) is enforced here.
/// </summary>
public interface IInvoicingService
{
    /// <summary>Raise a bill against the caller's order, due on the given calendar date. Returns the invoice id.</summary>
    Task<string> RaiseInvoiceForOrderAsync(int orderId, DateOnly dueDate, string buyerId, CancellationToken cancellationToken);

    /// <summary>Read a bill's current state (and, once issued, its pay link). Scoped to the caller unless operator.</summary>
    Task<InvoiceDetails> GetInvoiceAsync(string invoiceId, string requesterId, bool isOperator, CancellationToken cancellationToken);

    /// <summary>Correct the due date and/or customer details of a still-draft bill. Scoped to the caller unless operator.</summary>
    Task CorrectInvoiceAsync(string invoiceId, DateOnly? dueDate, CustomerDetails? customer, string requesterId, bool isOperator, CancellationToken cancellationToken);

    /// <summary>Operator action: put a bill to the shopper.</summary>
    Task IssueInvoiceAsync(string invoiceId, CancellationToken cancellationToken);

    /// <summary>Operator action: withdraw a bill so it is no longer payable.</summary>
    Task WithdrawInvoiceAsync(string invoiceId, CancellationToken cancellationToken);

    /// <summary>List the caller's own bills, each showing where it has got to.</summary>
    Task<IReadOnlyList<InvoiceSummary>> GetInvoicesForShopperAsync(string buyerId, CancellationToken cancellationToken);

    /// <summary>Operator action: reconcile the provider's record of bills in a range against eShop's own.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
