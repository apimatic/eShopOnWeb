using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A single line of a bill being raised with the provider.
/// </summary>
public record InvoiceProviderLineItem(string ProductName, string Sku, int Quantity, decimal UnitPrice);

/// <summary>
/// Everything the provider needs to raise (or re-state, when correcting) a bill. What is billed —
/// the amount and the line items — always originates from the order, never from a caller restating it.
/// </summary>
public record InvoiceProviderRequest(
    string InvoiceNumber,
    string Description,
    DateOnly DueDate,
    string Currency,
    decimal TotalAmount,
    IReadOnlyList<InvoiceProviderLineItem> LineItems,
    string CustomerName,
    string CustomerEmail,
    string CustomerReference);

/// <summary>
/// The provider's own account of a bill: its identifier there, where it currently stands, how it
/// reached that state, and — once it has been put to the shopper — how they can pay it.
/// </summary>
public record ProviderInvoiceState(
    string ProviderInvoiceId,
    string Status,
    string? PaymentLink,
    string? InvoiceNumber,
    decimal? TotalAmount,
    string? Currency,
    DateOnly? DueDate,
    IReadOnlyList<string> History);

/// <summary>
/// A summary of one bill the provider knows about, as returned when listing the provider's own record
/// of bills for reconciliation.
/// </summary>
public record ProviderInvoiceSummary(
    string ProviderInvoiceId,
    string? InvoiceNumber,
    string? CustomerReference,
    string Status,
    DateTimeOffset? CreatedDate,
    decimal? TotalAmount,
    string? Currency,
    string? CustomerName);

/// <summary>
/// The seam through which this application talks to the payment/invoicing provider (Visa, via its
/// CyberSource platform). Every provider interaction goes through here; nothing above this interface
/// knows which provider is behind it. Implementations translate provider faults into
/// <see cref="Exceptions.InvoiceProviderException"/>.
/// </summary>
public interface IInvoiceProvider
{
    /// <summary>Raises a new bill with the provider. The bill starts out not yet put to the shopper.</summary>
    Task<ProviderInvoiceState> RaiseAsync(InvoiceProviderRequest request, CancellationToken cancellationToken = default);

    /// <summary>Re-states an already-raised bill (its due date and customer details) with the provider.</summary>
    Task<ProviderInvoiceState> UpdateAsync(string providerInvoiceId, InvoiceProviderRequest request, CancellationToken cancellationToken = default);

    /// <summary>Asks the provider for the current state of a bill.</summary>
    Task<ProviderInvoiceState> GetAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>Puts the bill to the shopper. Afterwards the provider can hand out a way to pay it.</summary>
    Task<ProviderInvoiceState> IssueAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>Withdraws the bill so it is no longer payable.</summary>
    Task<ProviderInvoiceState> WithdrawAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of every bill it raised whose creation falls within the given
    /// range, covering the whole range. This includes bills that are not this application's, since the
    /// provider account is shared.
    /// </summary>
    Task<IReadOnlyList<ProviderInvoiceSummary>> ListRaisedBetweenAsync(DateTimeOffset fromInclusive, DateTimeOffset toInclusive, CancellationToken cancellationToken = default);
}
