using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int PayPalTransactionCount { get; set; }
    public int EShopPaidOrderCount { get; set; }
    public int MatchedCount { get; set; }
    public int InPayPalNotEShopCount { get; set; }
    public int InEShopNotPayPalCount { get; set; }

    /// <summary>Transactions PayPal reported that are backed by an eShop order.</summary>
    public List<ReconciliationLineDto> Matched { get; set; } = new();

    /// <summary>Transactions PayPal knows about that eShop has no order for.</summary>
    public List<ReconciliationLineDto> InPayPalNotEShop { get; set; } = new();

    /// <summary>eShop payments in the window that PayPal has not (yet) reported.</summary>
    public List<ReconciliationLineDto> InEShopNotPayPal { get; set; } = new();
}

public class ReconciliationLineDto
{
    public string? PaymentReference { get; set; }
    public string? PayPalTransactionId { get; set; }
    public string? PayPalEventCode { get; set; }
    public string? PayPalStatus { get; set; }
    public decimal? PayPalAmount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? PayPalDate { get; set; }
    public int? OrderId { get; set; }
    public string? OrderStatus { get; set; }
    public decimal? OrderAmount { get; set; }
    public string Note { get; set; } = string.Empty;
}
