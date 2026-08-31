using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>Customer details a caller may supply when raising or correcting a bill. Both are optional;
/// when omitted the shopper's own identity is used.</summary>
public record InvoiceCustomerDetails(string? Name, string? Email);

/// <summary>A bill paired with the provider's live view of it — what a shopper reads back.</summary>
public record InvoiceDetails(Invoice Invoice, ProviderInvoice Provider);

/// <summary>
/// Orchestrates the invoicing flows: raising a bill against an order, reading and correcting it, putting it
/// to the shopper and taking it back, listing a shopper's bills, and the operator's reconciliation report.
/// Shopper-scoped operations act only on the caller's own data; operator operations act on any bill.
/// A null return means "no such bill for this caller" (the endpoint answers 404).
/// </summary>
public interface IInvoiceService
{
    /// <summary>Raise a bill for one of the caller's own orders. Returns null if the order is not the caller's.</summary>
    Task<Invoice?> RaiseInvoiceForOrderAsync(int orderId, string buyerId, DateOnly dueDate,
        InvoiceCustomerDetails? customer, CancellationToken cancellationToken = default);

    /// <summary>Read one of the caller's own bills, refreshed from the provider. Returns null if not the caller's.</summary>
    Task<InvoiceDetails?> GetInvoiceForBuyerAsync(string providerInvoiceId, string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Correct the due date/customer of one of the caller's own draft bills. Returns null if not the caller's;
    /// throws when the bill is no longer a draft.</summary>
    Task<Invoice?> CorrectDraftInvoiceAsync(string providerInvoiceId, string buyerId, DateOnly? dueDate,
        InvoiceCustomerDetails? customer, CancellationToken cancellationToken = default);

    /// <summary>Operator: put any bill to the shopper. Returns null if no such bill; throws if it is not a draft.</summary>
    Task<Invoice?> IssueInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>Operator: withdraw any bill. Returns null if no such bill; throws if it is already withdrawn.</summary>
    Task<Invoice?> WithdrawInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>The caller's own bills, each showing where it has got to.</summary>
    Task<IReadOnlyList<Invoice>> GetInvoicesForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Operator: reconcile the provider's record of bills raised in a range against eShop's own.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
