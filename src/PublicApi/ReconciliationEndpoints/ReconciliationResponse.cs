using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationMatch> Matched { get; set; } = new();
    public List<PayPalTransactionRecord> PayPalOnly { get; set; } = new();
    public List<EshopUnmatchedOrder> EshopOnly { get; set; } = new();
}
