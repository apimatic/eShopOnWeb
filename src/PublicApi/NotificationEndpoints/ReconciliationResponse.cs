using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi;

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
    public ReconciliationResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ReconciliationResponse()
    {
    }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationRowDto> Matched { get; set; } = new();
    public List<ReconciliationRowDto> ProviderOnly { get; set; } = new();
    public List<ReconciliationRowDto> ApplicationOnly { get; set; } = new();
}

public class ReconciliationRowDto
{
    public string? ProviderSid { get; set; }
    public int? NotificationId { get; set; }
    public string? Kind { get; set; }
    public string? ProviderStatus { get; set; }
    public string? ApplicationStatus { get; set; }
    public string? DateSent { get; set; }
    public string Match { get; set; } = string.Empty;
}
