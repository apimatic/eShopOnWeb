using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Lines up PayPal's own record of transactions for a date range against eShop's
/// payments, so a transaction one side knows about and the other doesn't is visible.
/// </summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

/// <summary>An eShop-side money movement (a capture or a refund) that carries a PayPal id.</summary>
public record EShopTransaction(string TransactionId, int OrderId, string Kind, decimal Amount, string Currency, string Status);

/// <summary>A PayPal transaction matched to an eShop record by id.</summary>
public record ReconciliationMatch(string TransactionId, int OrderId, string Kind, decimal? PayPalAmount, decimal EShopAmount, bool AmountsAgree);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int EShopTransactionCount,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<GatewayTransaction> InPayPalNotInEShop,
    IReadOnlyList<EShopTransaction> InEShopNotInPayPal);
