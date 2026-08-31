using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the invoicing flows: raising a bill from an order, reading and correcting it, putting it
/// to the shopper and taking it back, and reconciling against the provider. Enforces that a bill belongs
/// to the shopper whose order it was raised against.
/// </summary>
public interface IInvoiceService
{
    /// <summary>
    /// Raise a bill with the provider for one of the caller's own orders. What is billed comes from the
    /// order. The bill starts out not yet put to the shopper.
    /// </summary>
    Task<Invoice> RaiseInvoiceForOrderAsync(int orderId, string buyerId, DateOnly dueDate, CustomerDetails? customerOverride, CancellationToken cancellationToken = default);

    /// <summary>
    /// Read one of the caller's own bills: its state, what the provider reports about how it got there,
    /// and — once issued — how it can be paid.
    /// </summary>
    Task<InvoiceView> GetInvoiceForBuyerAsync(string invoiceId, string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Correct the due date and/or customer details on one of the caller's own draft bills. Fails if the
    /// bill has already been put to the shopper or withdrawn.
    /// </summary>
    Task CorrectInvoiceForBuyerAsync(string invoiceId, string buyerId, DateOnly? dueDate, CustomerDetails? customer, CancellationToken cancellationToken = default);

    /// <summary>The caller's own bills, each showing where it has got to.</summary>
    Task<IReadOnlyList<Invoice>> GetInvoicesForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: put any shopper's bill to them.</summary>
    Task<Invoice> IssueInvoiceAsync(string invoiceId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: withdraw any shopper's bill.</summary>
    Task<Invoice> WithdrawInvoiceAsync(string invoiceId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: reconcile the provider's record against eShop's over a date range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
