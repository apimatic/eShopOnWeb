using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ReconciliationRequest : BaseRequest
{
    [JsonIgnore]
    public DateTimeOffset From { get; set; }

    [JsonIgnore]
    public DateTimeOffset To { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
    public int TotalPayPalTransactions { get; set; }
    public int TotalMatched { get; set; }
    public int TotalUnmatchedPayPal { get; set; }
    public int TotalUnmatchedEShop { get; set; }
    public List<ReconciliationEntry> Transactions { get; set; } = new();
    public List<UnmatchedEShopPayment> UnmatchedEShopPayments { get; set; } = new();
}
