using System;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }

    public ReconciliationRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; }
    public bool Truncated { get; set; }
    public System.Collections.Generic.List<ReconciliationRowDto> Matched { get; set; } = new();
    public System.Collections.Generic.List<ReconciliationRowDto> ProviderOnly { get; set; } = new();
    public System.Collections.Generic.List<ReconciliationRowDto> EShopOnly { get; set; } = new();
}

public class ReconciliationRowDto
{
    public int? NotificationId { get; set; }
    public string ProviderSid { get; set; }
    public string ProviderStatus { get; set; }
    public string EShopStatus { get; set; }
    public int? OrderId { get; set; }
}
