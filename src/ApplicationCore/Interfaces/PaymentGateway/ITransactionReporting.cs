using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentGateway;

/// <summary>
/// PayPal's own record of transactions over a date range (Transaction Search v1), used to
/// reconcile against eShop orders. The implementation must cover the whole range — chunking
/// into PayPal's maximum window and paging every page — not just the first page.
/// </summary>
public interface ITransactionReporting
{
    Task<IReadOnlyList<GatewayTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
