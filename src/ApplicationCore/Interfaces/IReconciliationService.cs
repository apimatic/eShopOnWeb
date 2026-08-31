using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Builds a reconciliation report: PayPal's own record of transactions for a date
/// range lined up against eShop orders, so a transaction only one side knows about
/// is visible.
/// </summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> GetReportAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
