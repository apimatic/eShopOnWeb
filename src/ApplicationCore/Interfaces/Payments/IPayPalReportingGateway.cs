using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>
/// Abstraction over the PayPal Transaction Search v1 API. Implemented in the infrastructure layer,
/// which handles the contract's 31-day window limit and page-based pagination so callers get the
/// whole date range, not just the first page.
/// </summary>
public interface IPayPalReportingGateway
{
    /// <summary>
    /// Returns every transaction PayPal has recorded in [from, to], transparently splitting the
    /// range into windows within PayPal's limit and paging through each window.
    /// </summary>
    Task<IReadOnlyList<ReportedTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
