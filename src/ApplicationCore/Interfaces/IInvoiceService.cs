using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Customer invoicing for eShop, backed by the Visa/CyberSource billing provider. Every method that
/// can hit an ownership or state boundary returns a <see cref="ServiceResult{T}"/>; provider and
/// transport faults are raised as <see cref="Exceptions.InvoiceProviderException"/>.
/// </summary>
public interface IInvoiceService
{
    /// <summary>Raise a bill with the provider for an order the buyer owns. The billed amount and items
    /// come from the order itself. Returns the new bill's identifier and initial (draft) state.</summary>
    Task<ServiceResult<InvoiceDetails>> RaiseInvoiceAsync(int orderId, string buyerId, DateTimeOffset dueDate,
        string? customerName, string? customerEmail, CancellationToken cancellationToken);

    /// <summary>Read a bill the buyer owns, refreshed from the provider (state, history, pay link).</summary>
    Task<ServiceResult<InvoiceDetails>> GetInvoiceAsync(string invoiceId, string buyerId,
        CancellationToken cancellationToken);

    /// <summary>Correct the due date and/or customer details on a still-draft bill the buyer owns.
    /// The amount is not correctable. Refused (Conflict) once the bill is issued or withdrawn.</summary>
    Task<ServiceResult<InvoiceDetails>> CorrectInvoiceAsync(string invoiceId, string buyerId,
        DateTimeOffset? dueDate, string? customerName, string? customerEmail, CancellationToken cancellationToken);

    /// <summary>Operator action: put a bill to the shopper. Afterwards a pay link can be handed out.</summary>
    Task<ServiceResult<InvoiceDetails>> IssueInvoiceAsync(string invoiceId, CancellationToken cancellationToken);

    /// <summary>Operator action: withdraw a bill. Afterwards it is no longer payable.</summary>
    Task<ServiceResult<InvoiceDetails>> WithdrawInvoiceAsync(string invoiceId, CancellationToken cancellationToken);

    /// <summary>The caller's own bills, each showing where it has got to.</summary>
    Task<IReadOnlyList<InvoiceSummary>> GetInvoicesForBuyerAsync(string buyerId, CancellationToken cancellationToken);

    /// <summary>Operator action: reconcile the provider's record of bills raised in a range against
    /// eShop's own, making plain which are eShop's and which are the account's other activity.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken);
}
