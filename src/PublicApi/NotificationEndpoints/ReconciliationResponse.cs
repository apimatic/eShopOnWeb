using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationEntryDto
{
    public string Match { get; set; } = string.Empty;
    public int? NotificationId { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string? EShopStatus { get; set; }
    public string? ProviderStatus { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public List<ReconciliationEntryDto> Entries { get; set; } = new();
}
