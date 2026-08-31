using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IReconciliationService
{
    /// <summary>
    /// Lines up the gateway's own record of transactions over [from, to] against eShop
    /// orders/payments, so a payment known on only one side is visible.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public class ReconciliationReport
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationEntry> Entries { get; set; } = new List<ReconciliationEntry>();
    public int MatchedCount { get; set; }
    public int OnlyInGatewayCount { get; set; }
    public int OnlyInShopCount { get; set; }
}

public class ReconciliationEntry
{
    /// <summary>Matched, OnlyInGateway (PayPal knows it, eShop doesn't) or OnlyInShop (reverse).</summary>
    public string MatchStatus { get; set; } = string.Empty;

    // Gateway side
    public string? GatewayTransactionId { get; set; }
    public string? GatewayReferenceId { get; set; }
    public string? GatewayEventCode { get; set; }
    public DateTimeOffset? GatewayDate { get; set; }
    public decimal? GatewayAmount { get; set; }
    public decimal? GatewayFee { get; set; }
    public string? GatewayStatus { get; set; }

    // eShop side
    public int? OrderId { get; set; }
    public int? PaymentId { get; set; }
    public string? ShopPaymentStatus { get; set; }
    public decimal? ShopAmount { get; set; }
    public string? Currency { get; set; }
}
