using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationQuery : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public IReadOnlyList<ReconciliationMatch> Matched { get; set; } = Array.Empty<ReconciliationMatch>();
    public IReadOnlyList<ReconciliationPayPalOnly> PayPalOnly { get; set; } = Array.Empty<ReconciliationPayPalOnly>();
    public IReadOnlyList<ReconciliationEshopOnly> EshopOnly { get; set; } = Array.Empty<ReconciliationEshopOnly>();
}
