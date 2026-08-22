using System;
using System.Linq;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationResponse : BaseResponse
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;
    public bool Complete { get; set; }
    public ReconciliationItemDto[] Entries { get; set; } = Array.Empty<ReconciliationItemDto>();
}

public class ReconciliationItemDto
{
    public string? ProviderSid { get; set; }
    public string? Status { get; set; }
    public string? From { get; set; }
    public string? Body { get; set; }
    public string? DateSent { get; set; }
    public string? DateCreated { get; set; }
    public int? NotificationId { get; set; }
    public bool InProvider { get; set; }
    public bool InApplication { get; set; }
    public bool Matched => InProvider && InApplication;
}
