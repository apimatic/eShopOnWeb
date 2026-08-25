using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationReportResponse : BaseResponse
{
    public ReconciliationReportResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ReconciliationReportResponse()
    {
    }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationEntry> Entries { get; set; } = new();
}
