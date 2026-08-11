using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>
/// Produces a reconciliation report over a date range: PayPal's own transaction records lined up against
/// eShop's captured orders, surfacing anything present on one side but not the other. Covers the whole
/// range (all pages), not just the first page.
/// </summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
