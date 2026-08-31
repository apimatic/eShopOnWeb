using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the billing flows over the order model, the invoice store, and the invoicing provider.
/// Shopper-scoped methods take the caller's <c>buyerId</c> and act only on that shopper's data; operator
/// methods (issue/withdraw/reconcile) act on any shopper's bill and take no <c>buyerId</c>.
/// </summary>
public interface IInvoiceService
{
    /// <summary>Raise a draft bill with the provider for the shopper's own order.</summary>
    Task<Invoice> RaiseInvoiceForOrderAsync(int orderId, string buyerId, DateTimeOffset dueDate, CancellationToken cancellationToken = default);

    /// <summary>Read one of the shopper's own bills, merged with the live provider-owned state.</summary>
    Task<InvoiceDetails> GetInvoiceForShopperAsync(int invoiceId, string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Correct the due date / customer details of one of the shopper's own draft bills.</summary>
    Task<Invoice> CorrectInvoiceAsync(int invoiceId, string buyerId, InvoiceCorrectionRequest correction, CancellationToken cancellationToken = default);

    /// <summary>Operator: put a bill to the shopper.</summary>
    Task<Invoice> IssueInvoiceAsync(int invoiceId, CancellationToken cancellationToken = default);

    /// <summary>Operator: withdraw a bill so it is no longer payable.</summary>
    Task<Invoice> WithdrawInvoiceAsync(int invoiceId, CancellationToken cancellationToken = default);

    /// <summary>The shopper's own bills, each showing where it has got to.</summary>
    Task<IReadOnlyList<Invoice>> GetInvoicesForShopperAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Operator: reconcile the provider's own record of bills raised in a range against eShop's.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>A requested correction. Only the non-null fields are applied; the amount is never correctable.</summary>
public record InvoiceCorrectionRequest(DateTimeOffset? DueDate, string? CustomerName, string? CustomerEmail);

/// <summary>A bill merged with the live provider-owned state for the read endpoint.</summary>
public record InvoiceDetails(
    Invoice Invoice,
    string? ProviderStatus,
    string? PaymentLink,
    IReadOnlyList<ProviderInvoiceHistoryEntry> History);

/// <summary>How a single row of the reconciliation report lines up between the provider and eShop.</summary>
public enum ReconciliationStatus
{
    /// <summary>eShop's bill, present at the provider and in eShop's store.</summary>
    Reconciled,

    /// <summary>An eShop-tagged bill the provider has, that eShop has no record of (e.g. lost across a restart).</summary>
    MissingFromEShop,

    /// <summary>A bill eShop raised that the provider's record for this range does not show.</summary>
    MissingFromProvider,

    /// <summary>A bill on the provider account that is not eShop's at all.</summary>
    ForeignProviderInvoice
}

/// <summary>One reconciliation row.</summary>
public record ReconciliationEntry(
    ReconciliationStatus Status,
    bool BelongsToEShop,
    bool PresentAtProvider,
    bool PresentInEShop,
    int? InvoiceId,
    string? ProviderInvoiceId,
    string? MerchantCustomerId,
    string? ProviderStatus,
    InvoiceStatus? EShopStatus,
    decimal? Amount,
    string? Currency,
    DateTimeOffset? CreatedDate);

/// <summary>Headline counts for the reconciliation report.</summary>
public record ReconciliationSummary(
    int ProviderInvoiceCount,
    int EShopInvoiceCount,
    int ReconciledCount,
    int MissingFromEShopCount,
    int MissingFromProviderCount,
    int ForeignProviderInvoiceCount);

/// <summary>The reconciliation report over a date range.</summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    ReconciliationSummary Summary,
    IReadOnlyList<ReconciliationEntry> Entries,
    string Note);
