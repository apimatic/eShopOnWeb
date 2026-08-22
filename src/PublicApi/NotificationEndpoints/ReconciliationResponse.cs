using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public bool Truncated { get; set; }
    public List<ReconciledMessageDto> Matched { get; set; } = new();
    public List<ReconciledMessageDto> ProviderOnly { get; set; } = new();
    public List<ReconciledMessageDto> ApplicationOnly { get; set; } = new();
}

public class ReconciledMessageDto
{
    public string? ProviderSid { get; set; }
    public int? NotificationId { get; set; }
    public string? ProviderStatus { get; set; }
    public string? ApplicationStatus { get; set; }
    public string? DateSent { get; set; }
}
