using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A gateway to the external invoicing provider (Visa / CyberSource). This abstraction keeps the
/// domain and application layers free of any provider SDK: the concrete implementation in the
/// Infrastructure layer maps these domain-friendly shapes onto the provider's API.
/// </summary>
public interface IInvoiceProvider
{
    /// <summary>Raise a new bill with the provider. The bill starts out as a draft.</summary>
    Task<ProviderInvoiceRef> CreateDraftAsync(ProviderInvoiceDraft draft, CancellationToken cancellationToken = default);

    /// <summary>Read the provider's current view of a bill, including how it can be paid once issued.</summary>
    Task<ProviderInvoiceDetails> GetAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>Correct a draft bill's due date and customer details with the provider.</summary>
    Task<ProviderInvoiceRef> UpdateAsync(string providerInvoiceId, ProviderInvoiceUpdate update, CancellationToken cancellationToken = default);

    /// <summary>Put the bill to the shopper so it can be paid.</summary>
    Task<ProviderInvoiceRef> IssueAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>Withdraw the bill so it can no longer be paid.</summary>
    Task<ProviderInvoiceRef> WithdrawAsync(string providerInvoiceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// List the provider's own record of every bill it raised within the given window — including
    /// bills that are not this application's. The raised date is resolved from each bill's history.
    /// </summary>
    Task<IReadOnlyList<ProviderInvoiceSummary>> ListRaisedBetweenAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
