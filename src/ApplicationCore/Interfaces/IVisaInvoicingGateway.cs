using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Port onto the Visa invoicing provider (CyberSource). This is the only seam through which eShop
/// talks to Visa; the implementation lives in Infrastructure and routes every call through the
/// configured provider base address. It exposes each provider capability as a separate operation
/// so that raising, issuing and withdrawing a bill stay independently invocable.
/// </summary>
public interface IVisaInvoicingGateway
{
    /// <summary>Raise (create) a bill with the provider. The bill starts out not yet put to the shopper.</summary>
    Task<GatewayInvoice> RaiseAsync(GatewayInvoiceDraft draft, CancellationToken cancellationToken = default);

    /// <summary>Read the provider's current record of a bill, including how it can be paid once issued.</summary>
    Task<GatewayInvoice> GetAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>Correct the due date / customer details the provider holds for a bill. The amount comes from the order.</summary>
    Task<GatewayInvoice> CorrectAsync(string providerInvoiceId, GatewayInvoiceCorrection correction, CancellationToken cancellationToken = default);

    /// <summary>Put the bill to the shopper with the provider so a way to pay it can be handed out.</summary>
    Task<GatewayInvoice> IssueAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>Withdraw the bill with the provider so it is no longer payable.</summary>
    Task<GatewayInvoice> WithdrawAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// List the provider's own record of bills raised within a date range (inclusive). The provider's
    /// list endpoint carries no creation date and no date filter, so the implementation reads each
    /// bill's history to establish when it was raised and filters to the range here.
    /// </summary>
    Task<IReadOnlyList<GatewayInvoiceSummary>> ListRaisedBetweenAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
