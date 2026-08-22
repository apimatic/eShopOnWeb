using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class GetReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string? LastRefreshedDatetime { get; set; }
    public IReadOnlyList<ReconciliationMatch> Matched { get; set; } = Array.Empty<ReconciliationMatch>();
    public IReadOnlyList<PayPalTransactionRow> PayPalOnly { get; set; } = Array.Empty<PayPalTransactionRow>();
    public IReadOnlyList<EshopPaymentRow> EshopOnly { get; set; } = Array.Empty<EshopPaymentRow>();
}
