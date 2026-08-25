using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public List<ReconciliationRecord> Records { get; set; } = new();
}

public class ReconciliationRecord
{
    public int? OrderId { get; set; }
    public DateTimeOffset? OrderDate { get; set; }
    public decimal? OrderTotal { get; set; }
    public string? PayPalOrderId { get; set; }
    public string? TransactionId { get; set; }
    public string? Amount { get; set; }
    public string? Fee { get; set; }
    public string? Status { get; set; }
    public string? CreateTime { get; set; }
}
