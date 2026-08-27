using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IReconciliationService
{
    /// <summary>
    /// Lists the provider's own record of transactions for [from, to] and lines them up
    /// against eShop orders/payments. Covers the whole range (all pages, all 31-day windows).
    /// </summary>
    Task<ReconciliationReport> GetReportAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
