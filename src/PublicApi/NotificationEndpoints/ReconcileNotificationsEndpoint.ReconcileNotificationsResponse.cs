using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconcileNotificationsResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int LocalOnlyCount { get; set; }
    public List<ReconciledMessageDto> Matched { get; set; } = new();
    public List<ProviderOnlyMessageDto> ProviderOnly { get; set; } = new();
    public List<LocalOnlyMessageDto> LocalOnly { get; set; } = new();
}

public class ReconciledMessageDto
{
    public int NotificationId { get; set; }
    public string ProviderMessageSid { get; set; } = string.Empty;
    public string LocalStatus { get; set; } = string.Empty;
    public string ProviderStatus { get; set; } = string.Empty;
}

public class ProviderOnlyMessageDto
{
    public string ProviderMessageSid { get; set; } = string.Empty;
    public string ProviderStatus { get; set; } = string.Empty;
    public string? DateSent { get; set; }
}

public class LocalOnlyMessageDto
{
    public int NotificationId { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string LocalStatus { get; set; } = string.Empty;
}
