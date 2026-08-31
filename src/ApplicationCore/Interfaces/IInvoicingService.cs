using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Coordinates eShop's invoicing: raising a bill against an order with the provider, reading and correcting
/// it, putting it to the shopper and taking it back, and reconciling the provider's record against eShop's.
/// Shopper-scoped operations enforce that a caller only ever touches their own bills; operator operations
/// (issue, withdraw, reconcile) act on any bill.
/// </summary>
public interface IInvoicingService
{
    /// <summary>
    /// Raises a bill with the provider for the caller's order, held in draft. What is billed comes from the
    /// order itself. Returns the provider invoice id. Throws if the order is not the caller's, or if it has
    /// already been billed.
    /// </summary>
    Task<string> RaiseInvoiceAsync(int orderId, string buyerId, DateTimeOffset dueDate,
        string? customerName, string? customerEmail, CancellationToken cancellationToken = default);

    /// <summary>Reads one of the caller's bills, refreshing its provider status/history/payment-link.</summary>
    Task<InvoiceDetails> GetInvoiceForShopperAsync(string invoiceId, string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Corrects the due date and/or customer details on one of the caller's bills, only while it is still a
    /// draft. The billed amount is not correctable. Throws <see cref="Exceptions.InvoiceStateException"/> if
    /// the bill has already been put to the shopper or withdrawn.
    /// </summary>
    Task<InvoiceDetails> CorrectInvoiceAsync(string invoiceId, string buyerId, DateTimeOffset? dueDate,
        string? customerName, string? customerEmail, CancellationToken cancellationToken = default);

    /// <summary>Operator action: puts a bill to the shopper, after which a payment link is available.</summary>
    Task<InvoiceDetails> IssueInvoiceAsync(string invoiceId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: withdraws a bill so it is no longer payable.</summary>
    Task<InvoiceDetails> WithdrawInvoiceAsync(string invoiceId, CancellationToken cancellationToken = default);

    /// <summary>Lists the caller's own bills, each showing where it has got to.</summary>
    Task<IReadOnlyList<InvoiceSummaryView>> ListInvoicesForShopperAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action: reconciles the provider's own record of bills raised in a date range against
    /// eShop's, distinguishing eShop's bills from those raised by other activity on the account.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
