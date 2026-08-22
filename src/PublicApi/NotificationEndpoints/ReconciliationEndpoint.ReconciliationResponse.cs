using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationEntryDto
{
    public int? NotificationId { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string? LocalStatus { get; set; }
    public string? ProviderStatus { get; set; }
    public string Match { get; set; } = string.Empty;
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationEntryDto> Matched { get; set; } = new();
    public List<ReconciliationEntryDto> ProviderOnly { get; set; } = new();
    public List<ReconciliationEntryDto> LocalOnly { get; set; } = new();
}
