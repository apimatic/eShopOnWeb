using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>Lines PayPal's own transaction record up against eShop orders for a date range.</summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
