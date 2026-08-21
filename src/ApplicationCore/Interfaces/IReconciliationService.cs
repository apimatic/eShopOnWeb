using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IReconciliationService
{
    Task<IReadOnlyList<ReconciliationLine>> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct);
}
