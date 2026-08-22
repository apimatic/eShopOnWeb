using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationMatchDto
{
    public int NotificationId { get; set; }
    public string ProviderMessageSid { get; set; } = string.Empty;
    public string? EshopStatus { get; set; }
    public string? ProviderStatus { get; set; }
}

public class ProviderOnlyMessageDto
{
    public string ProviderMessageSid { get; set; } = string.Empty;
    public string? ProviderStatus { get; set; }
    public DateTimeOffset? DateSent { get; set; }
}

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
    public string FromNumber { get; set; } = string.Empty;
    public List<ReconciliationMatchDto> Matched { get; set; } = new();
    public List<ProviderOnlyMessageDto> ProviderOnly { get; set; } = new();
    public List<NotificationDto> EshopOnly { get; set; } = new();
}
