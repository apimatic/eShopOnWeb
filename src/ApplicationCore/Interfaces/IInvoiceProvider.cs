using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the external payment/invoicing provider (Visa, via its CyberSource platform).
/// Everything eShop knows about a bill that the provider owns is obtained by asking the provider
/// through this contract — the provider cannot call back into eShop.
///
/// Implementations translate provider refusals of illegitimate state transitions (for example,
/// withdrawing an already-withdrawn bill) into <see cref="ProviderOperationRefusedException"/>, so
/// callers can treat a refusal as an outcome of the bill's state rather than an integration fault.
/// </summary>
public interface IInvoiceProvider
{
    /// <summary>Raise a new bill with the provider. The bill starts as a draft (not yet put to the shopper).</summary>
    Task<ProviderInvoice> CreateInvoiceAsync(ProviderInvoiceRequest request, CancellationToken cancellationToken = default);

    /// <summary>Fetch the provider's current record of a bill, including how it can be paid once issued.</summary>
    Task<ProviderInvoice> GetInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>Correct a draft bill's due date / customer details (and re-assert the order-derived amount).</summary>
    Task<ProviderInvoice> UpdateInvoiceAsync(string providerInvoiceId, ProviderInvoiceRequest request, CancellationToken cancellationToken = default);

    /// <summary>Put the bill to the shopper. Afterwards the provider hands out a way to pay it.</summary>
    Task<ProviderInvoice> IssueInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>Withdraw a bill so it can no longer be paid.</summary>
    Task<ProviderInvoice> WithdrawInvoiceAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The provider's own record of every bill it has raised (across the whole account, including
    /// bills that are not eShop's), enriched with each bill's creation date so a date range can be
    /// applied. Used by operator reconciliation.
    /// </summary>
    Task<IReadOnlyList<ProviderInvoiceRecord>> ListInvoicesAsync(CancellationToken cancellationToken = default);
}

/// <summary>A request to raise or correct a bill with the provider. Amounts always come from the order.</summary>
public sealed record ProviderInvoiceRequest
{
    public required string InvoiceNumber { get; init; }
    public required string Description { get; init; }
    public required DateOnly DueDate { get; init; }
    public required string Currency { get; init; }
    public required decimal TotalAmount { get; init; }
    public string? CustomerName { get; init; }
    public string? CustomerEmail { get; init; }
    public IReadOnlyList<ProviderInvoiceLine> Lines { get; init; } = Array.Empty<ProviderInvoiceLine>();
}

public sealed record ProviderInvoiceLine(string ProductSku, string ProductName, int Quantity, decimal UnitPrice);

/// <summary>The provider's view of a single bill, as returned by create / get / issue / withdraw / correct.</summary>
public sealed record ProviderInvoice
{
    public required string Id { get; init; }
    public string? InvoiceNumber { get; init; }
    public required string Status { get; init; }
    public string? PaymentLink { get; init; }
    public decimal? TotalAmount { get; init; }
    public string? Currency { get; init; }
    public DateOnly? DueDate { get; init; }
    public string? CustomerName { get; init; }
    public string? CustomerEmail { get; init; }
    public IReadOnlyList<ProviderInvoiceEvent> History { get; init; } = Array.Empty<ProviderInvoiceEvent>();
}

/// <summary>A single step in how a bill reached its current state, as reported by the provider.</summary>
public sealed record ProviderInvoiceEvent(string Event, DateTimeOffset? Date);

/// <summary>A provider bill as it appears in reconciliation: its identity, state, and when it was raised.</summary>
public sealed record ProviderInvoiceRecord
{
    public required string Id { get; init; }
    public required string Status { get; init; }
    public DateTimeOffset? CreatedDate { get; init; }
    public decimal? TotalAmount { get; init; }
    public string? Currency { get; init; }
    public DateOnly? DueDate { get; init; }
    public string? CustomerName { get; init; }
}
