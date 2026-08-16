using System;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Builds the reconciliation report between PayPal's transactions and eShop orders.</summary>
public interface IReconciliationService
{
    Task<Result<ReconciliationReport>> ReconcileAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
