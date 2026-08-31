using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates customer invoicing: turning an eShop order into a bill held with the provider,
/// correcting it while it is still a draft, putting it to the shopper and taking it back, and
/// reconciling what the provider has raised against what eShop believes it raised.
///
/// Shopper-scoped operations take the caller's <c>buyerId</c> and act only on that shopper's data.
/// Operator operations act on any shopper's bill; authorization is enforced at the HTTP boundary.
/// </summary>
public interface IInvoiceService
{
    // --- Shopper-scoped ---

    /// <summary>Raise a bill with the provider for one of the caller's own orders.</summary>
    Task<InvoiceResult> RaiseInvoiceAsync(int orderId, string buyerId, DateOnly dueDate, CancellationToken cancellationToken = default);

    /// <summary>Read the current state of one of the caller's own bills, including how to pay it once issued.</summary>
    Task<InvoiceDetail> GetInvoiceForBuyerAsync(string providerInvoiceId, string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Correct the due date / customer details of one of the caller's own draft bills.</summary>
    Task<InvoiceDetail> CorrectInvoiceAsync(string providerInvoiceId, string buyerId, InvoiceCorrection correction, CancellationToken cancellationToken = default);

    /// <summary>List all of the caller's own bills, each showing where it has got to.</summary>
    Task<IReadOnlyList<InvoiceSummary>> GetInvoicesForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    // --- Operator ---

    /// <summary>Put a bill to the shopper. Afterwards a way to pay it can be handed out.</summary>
    Task<InvoiceDetail> IssueInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>Withdraw a bill so it can no longer be paid.</summary>
    Task<InvoiceDetail> WithdrawInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Line up the provider's own record of bills raised in a date range against what eShop believes
    /// it raised, making plain which bills are eShop's and which belong to other activity on the
    /// shared provider account.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>A correction to a draft bill. The amount is never here — it always comes from the order.</summary>
public sealed record InvoiceCorrection
{
    public DateOnly? DueDate { get; init; }
    public string? CustomerName { get; init; }
    public string? CustomerEmail { get; init; }
}

/// <summary>The outcome of raising a bill.</summary>
public sealed record InvoiceResult
{
    public required string InvoiceId { get; init; }
    public required int OrderId { get; init; }
    public required string Status { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required DateOnly DueDate { get; init; }
}

/// <summary>The full current state of a bill, blending eShop's record with the provider's own report.</summary>
public sealed record InvoiceDetail
{
    public required string InvoiceId { get; init; }
    public required int OrderId { get; init; }
    public required string Status { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required DateOnly DueDate { get; init; }
    public string? CustomerName { get; init; }
    public string? CustomerEmail { get; init; }
    public bool IsIssued { get; init; }
    public bool IsWithdrawn { get; init; }

    /// <summary>How the shopper can pay the bill. Present only once it has been put to them and not withdrawn.</summary>
    public string? PaymentLink { get; init; }

    /// <summary>Whatever the provider reports about how the bill reached its current state.</summary>
    public IReadOnlyList<InvoiceHistoryEntry> History { get; init; } = Array.Empty<InvoiceHistoryEntry>();
}

public sealed record InvoiceHistoryEntry(string Event, DateTimeOffset? Date);

/// <summary>A bill in the caller's list of bills.</summary>
public sealed record InvoiceSummary
{
    public required string InvoiceId { get; init; }
    public required int OrderId { get; init; }
    public required string Status { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required DateOnly DueDate { get; init; }
    public bool IsIssued { get; init; }
    public bool IsWithdrawn { get; init; }
}

/// <summary>Operator reconciliation over a date range.</summary>
public sealed record ReconciliationReport
{
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }
    public required IReadOnlyList<ReconciliationEntry> Entries { get; init; }
    public required ReconciliationSummary Summary { get; init; }
}

public sealed record ReconciliationSummary
{
    /// <summary>Total bills in the report (provider bills in range plus any eShop bills the provider does not report).</summary>
    public required int Total { get; init; }
    /// <summary>eShop bills present both at the provider and in eShop's own records.</summary>
    public required int InSync { get; init; }
    /// <summary>eShop bills the provider reports but eShop has no local record of.</summary>
    public required int AtProviderNotInEShop { get; init; }
    /// <summary>eShop bills eShop records but the provider does not report in range.</summary>
    public required int InEShopNotAtProvider { get; init; }
    /// <summary>Bills on the provider account that are not this application's.</summary>
    public required int External { get; init; }
}

public sealed record ReconciliationEntry
{
    public required string InvoiceId { get; init; }

    /// <summary><c>true</c> when this bill was raised by eShop; <c>false</c> when it belongs to other activity on the account.</summary>
    public required bool IsEShopInvoice { get; init; }

    public required bool PresentAtProvider { get; init; }
    public required bool PresentInEShop { get; init; }

    /// <summary><c>true</c> when the provider and eShop do not agree on this bill's existence.</summary>
    public required bool IsDiscrepancy { get; init; }

    public string? Status { get; init; }
    public int? OrderId { get; init; }
    public decimal? Amount { get; init; }
    public string? Currency { get; init; }
    public DateOnly? DueDate { get; init; }
    public DateTimeOffset? CreatedDate { get; init; }
    public string? CustomerName { get; init; }
}
