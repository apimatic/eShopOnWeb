using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Builds a reconciliation report that lines PayPal's own transaction records for a date range up
/// against eShop's captured payments and refunds, surfacing either-side-only discrepancies.
/// </summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> BuildAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
