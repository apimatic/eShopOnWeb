using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Operator reconciliation: lists PayPal's own transaction record for a date range and lines it up
/// against eShop orders, so a payment PayPal knows about that eShop does not — or the reverse — is
/// visible. Covers the whole range, not just the first page.
/// </summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
