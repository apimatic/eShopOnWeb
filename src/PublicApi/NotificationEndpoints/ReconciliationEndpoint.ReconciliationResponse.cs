using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public bool Incomplete { get; set; }
    public List<ReconciliationMatchDto> Matched { get; set; } = new();
    public List<ReconciliationProviderEntryDto> ProviderOnly { get; set; } = new();
    public List<ReconciliationLocalEntryDto> LocalOnly { get; set; } = new();
}

public class ReconciliationMatchDto
{
    public int NotificationId { get; set; }
    public string ProviderSid { get; set; } = string.Empty;
    public string? Status { get; set; }
}

public class ReconciliationProviderEntryDto
{
    public string ProviderSid { get; set; } = string.Empty;
    public string? Status { get; set; }
    public string? DateSent { get; set; }
}

public class ReconciliationLocalEntryDto
{
    public int NotificationId { get; set; }
    public string? ProviderSid { get; set; }
    public string? Status { get; set; }
}
