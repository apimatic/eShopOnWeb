using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public List<ReconciledItemDto> Matched { get; set; } = new();
    public List<ProviderOnlyItemDto> ProviderOnly { get; set; } = new();
    public List<ApplicationOnlyItemDto> ApplicationOnly { get; set; } = new();
}

public class ReconciledItemDto
{
    public int NotificationId { get; set; }
    public string ProviderMessageSid { get; set; } = string.Empty;
    public string? ApplicationStatus { get; set; }
    public string? ProviderStatus { get; set; }
}

public class ProviderOnlyItemDto
{
    public string ProviderMessageSid { get; set; } = string.Empty;
    public string? ProviderStatus { get; set; }
    public DateTimeOffset? DateSent { get; set; }
}

public class ApplicationOnlyItemDto
{
    public int NotificationId { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string? ApplicationStatus { get; set; }
}
