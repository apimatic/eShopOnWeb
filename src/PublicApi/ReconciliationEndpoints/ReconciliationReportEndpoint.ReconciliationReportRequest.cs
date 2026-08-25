using System;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationReportRequest : BaseRequest
{
    public ReconciliationReportRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }

    public DateTimeOffset From { get; }
    public DateTimeOffset To { get; }
}
