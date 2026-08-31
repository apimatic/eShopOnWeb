using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>An eShop bill together with the provider's current account of it.</summary>
public record InvoiceView(Invoice Invoice, ProviderInvoiceState Provider);

/// <summary>How one line of the reconciliation report lines up the provider's record against eShop's.</summary>
public enum ReconciliationCategory
{
    /// <summary>An eShop bill the provider and eShop both have — they agree.</summary>
    Matched,

    /// <summary>An eShop-origin bill the provider has, but eShop's records do not.</summary>
    MissingFromEShop,

    /// <summary>A bill eShop believes it raised, but the provider does not know about.</summary>
    MissingFromProvider,

    /// <summary>A bill on the shared provider account that is not this application's.</summary>
    ForeignToEShop
}

/// <summary>One reconciled bill: what the provider says, lined up against what eShop believes.</summary>
public record ReconciliationEntry(
    string InvoiceId,
    string? InvoiceNumber,
    bool IsEShopInvoice,
    ReconciliationCategory Category,
    string? ProviderStatus,
    string? LocalStatus,
    int? OrderId,
    string? BuyerId,
    decimal? Amount,
    string? Currency,
    DateTimeOffset? CreatedDate);

/// <summary>The reconciliation report over a date range.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int ProviderInvoiceCount,
    int EShopInvoiceCount,
    IReadOnlyList<ReconciliationEntry> Entries);

/// <summary>
/// Orchestrates the invoicing flows: raising a bill against an order, reading and correcting it,
/// putting it to the shopper and taking it back, and reconciling eShop's record against the provider's.
/// Enforces that a bill belongs to the shopper whose order it was raised against, with operators able
/// to act on any shopper's bill.
/// </summary>
public interface IInvoiceService
{
    /// <summary>
    /// Raises a bill with the provider for the given order. What is billed comes from the order itself.
    /// The bill starts out not yet put to the shopper. The order must belong to <paramref name="buyerId"/>.
    /// </summary>
    Task<Invoice> RaiseInvoiceForOrderAsync(int orderId, string buyerId, DateOnly dueDate,
        string? customerName, string? customerEmail, CancellationToken cancellationToken = default);

    /// <summary>Reads a bill's current state (from the provider) along with eShop's record of it.</summary>
    Task<InvoiceView> GetInvoiceAsync(string invoiceId, string requestingBuyerId, bool isOperator,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Corrects the due date and/or customer details on a bill that has not yet been put to the shopper.
    /// The amount is not correctable. Refused (told to the caller) once the bill is issued or withdrawn.
    /// </summary>
    Task<InvoiceView> CorrectInvoiceAsync(string invoiceId, string requestingBuyerId, bool isOperator,
        DateOnly? dueDate, string? customerName, string? customerEmail, CancellationToken cancellationToken = default);

    /// <summary>Puts the bill to the shopper (operator action). Afterwards a way to pay it can be handed out.</summary>
    Task<InvoiceView> IssueInvoiceAsync(string invoiceId, CancellationToken cancellationToken = default);

    /// <summary>Withdraws the bill (operator action). Afterwards it is no longer payable.</summary>
    Task<InvoiceView> WithdrawInvoiceAsync(string invoiceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The caller's own bills, each with where it locally stands (Draft / Issued / Withdrawn). Full
    /// provider detail and the payment link are available per-bill via <see cref="GetInvoiceAsync"/>.
    /// </summary>
    Task<IReadOnlyList<Invoice>> GetInvoicesForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reconciles the provider's own record of bills raised in the range against what eShop believes it
    /// raised, making plain which bills are eShop's and which belong to other activity on the account.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
