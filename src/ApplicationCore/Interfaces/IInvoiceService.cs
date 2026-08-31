using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the billing lifecycle: raising a bill against an order with the provider, reading and
/// correcting it, putting it to the shopper and withdrawing it, and reconciling eShop's records
/// against the provider's. Shopper-scoped methods take the caller's buyer id and act only on that
/// shopper's data; operator methods act on any shopper's bill.
/// </summary>
public interface IInvoiceService
{
    /// <summary>
    /// Raise a bill with the provider for the shopper's order. The amount is taken from the order
    /// itself, not from the caller. Returns the provider invoice id, or <c>null</c> if the order does
    /// not exist or does not belong to the shopper.
    /// </summary>
    Task<string?> RaiseForOrderAsync(
        int orderId, string buyerId, DateOnly dueDate, CustomerDetails? customer, CancellationToken cancellationToken = default);

    /// <summary>The shopper's view of a bill, or <c>null</c> if it is not theirs / does not exist.</summary>
    Task<InvoiceView?> GetForShopperAsync(string invoiceId, string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Correct the due date and/or customer details of the shopper's draft bill. Returns <c>null</c>
    /// if the bill is not theirs / does not exist. Throws <see cref="Exceptions.InvoiceStateConflictException"/>
    /// if the bill has already been issued or withdrawn and can no longer be corrected.
    /// </summary>
    Task<InvoiceView?> CorrectForShopperAsync(
        string invoiceId, string buyerId, DateOnly? dueDate, CustomerDetails? customer, CancellationToken cancellationToken = default);

    /// <summary>The shopper's bills, each showing where it has got to.</summary>
    Task<IReadOnlyList<MyInvoiceView>> ListForShopperAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action: put an eShop bill to the shopper. Returns <c>null</c> if eShop has no record of
    /// the bill. Throws <see cref="Exceptions.InvoiceStateConflictException"/> if the provider refuses.
    /// </summary>
    Task<InvoiceView?> IssueAsync(string invoiceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action: withdraw an eShop bill so it is no longer payable. Returns <c>null</c> if eShop
    /// has no record of the bill. Throws <see cref="Exceptions.InvoiceStateConflictException"/> if the
    /// provider refuses (for example a bill that has already been paid).
    /// </summary>
    Task<InvoiceView?> WithdrawAsync(string invoiceId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: reconcile the provider's record against eShop's over a date range.</summary>
    Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);
}
