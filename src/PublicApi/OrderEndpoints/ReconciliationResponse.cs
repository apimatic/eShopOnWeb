using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ReconciliationResponse()
    {
    }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationRowDto> Rows { get; set; } = new List<ReconciliationRowDto>();
}

public class ReconciliationRowDto
{
    public string? PayPalTransactionId { get; set; }
    public string? PayPalReferenceId { get; set; }
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset? Date { get; set; }
    public decimal? GrossAmount { get; set; }
    public decimal? FeeAmount { get; set; }
    public decimal? NetAmount { get; set; }
    public string? Currency { get; set; }
    public string? PayerEmail { get; set; }
    public int? OrderId { get; set; }
    public string? OrderStatus { get; set; }
    public decimal? OrderTotal { get; set; }
    public string Relation { get; set; } = string.Empty;
}