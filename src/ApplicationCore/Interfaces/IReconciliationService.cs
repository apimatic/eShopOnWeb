using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Paypal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Lines up PayPal's own record of transactions for a date range against eShop orders, surfacing
/// anything present on only one side.
/// </summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> BuildAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
