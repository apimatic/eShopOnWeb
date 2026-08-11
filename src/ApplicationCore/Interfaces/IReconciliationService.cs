using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Builds the reconciliation report: PayPal's own record of transactions for a date range lined up against
/// eShop orders, so a payment one side knows about and the other does not is visible. Covers the whole range
/// (chunking into ≤ 31-day windows and paging each) rather than just the first page.
/// </summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
