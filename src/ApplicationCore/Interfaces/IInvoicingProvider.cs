using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The port through which eShop talks to the external invoicing provider (Visa, via CyberSource).
/// It is deliberately provider-neutral: it exposes only eShop concepts, so ApplicationCore carries no
/// dependency on the provider SDK. The single implementation lives in Infrastructure.
///
/// Because the provider cannot call back into this application, everything eShop needs to know about a
/// bill is obtained by asking the provider through these methods — never by receiving a notification.
/// </summary>
public interface IInvoicingProvider
{
    /// <summary>Raise a bill with the provider. It starts out a draft — not yet put to the shopper.</summary>
    Task<ProviderInvoiceResult> RaiseAsync(RaiseInvoiceRequest request, CancellationToken cancellationToken = default);

    /// <summary>Ask the provider for a bill's current state, how it reached it, and its payment link.</summary>
    Task<ProviderInvoiceResult> GetAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>Correct a draft bill's due date and customer details. The provider refuses this once the bill is sent/canceled.</summary>
    Task<ProviderInvoiceResult> CorrectAsync(string providerInvoiceId, CorrectInvoiceRequest request, CancellationToken cancellationToken = default);

    /// <summary>Put the bill to the shopper. Afterwards a payment link is available.</summary>
    Task<ProviderInvoiceResult> IssueAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>Withdraw the bill so it is no longer payable.</summary>
    Task<ProviderInvoiceResult> WithdrawAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The provider's own record of every bill on the account. This is account-wide: it includes bills that
    /// are not this application's, so the caller must tell eShop's bills apart itself (see
    /// <see cref="ProviderInvoiceSummary.MerchantCustomerId"/>). The provider exposes no date-range filter and
    /// no per-invoice creation date on this projection, so date-range narrowing is the caller's job and can
    /// only be done against records the caller itself dated.
    /// </summary>
    Task<IReadOnlyList<ProviderInvoiceSummary>> ListAllInvoicesAsync(CancellationToken cancellationToken = default);
}

/// <summary>A single line on a bill, sourced from an order item.</summary>
public record InvoiceLineItem(string ProductName, string? Sku, int Quantity, decimal UnitPrice, decimal TotalAmount);

/// <summary>Everything the provider needs to raise a bill. What is billed comes from the order.</summary>
public record RaiseInvoiceRequest(
    string Description,
    decimal Amount,
    string Currency,
    DateTimeOffset DueDate,
    string CustomerName,
    string CustomerEmail,
    string MerchantCustomerId,
    IReadOnlyList<InvoiceLineItem> LineItems);

/// <summary>
/// A correction to a draft bill. The provider's update replaces the whole bill body, so the amount block
/// must be re-supplied unchanged (it is re-read from the order, not restated by the caller).
/// </summary>
public record CorrectInvoiceRequest(
    string Description,
    decimal Amount,
    string Currency,
    DateTimeOffset DueDate,
    string CustomerName,
    string CustomerEmail,
    string MerchantCustomerId);

/// <summary>One entry in the provider's account of how a bill reached its current state.</summary>
public record ProviderInvoiceHistoryEntry(string? Event, DateTimeOffset? Date);

/// <summary>The provider-owned state of a single bill, as read back from the provider.</summary>
public record ProviderInvoiceResult(
    string ProviderInvoiceId,
    string? Status,
    string? PaymentLink,
    IReadOnlyList<ProviderInvoiceHistoryEntry> History);

/// <summary>The provider's list-projection of one bill, used for reconciliation.</summary>
public record ProviderInvoiceSummary(
    string ProviderInvoiceId,
    string? Status,
    string? CreatedDateRaw,
    DateTimeOffset? CreatedDate,
    string? MerchantCustomerId,
    decimal? Amount,
    string? Currency);
