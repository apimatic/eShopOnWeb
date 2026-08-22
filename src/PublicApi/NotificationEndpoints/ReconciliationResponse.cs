using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

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
    public string FromNumber { get; set; } = string.Empty;
    public ReconciliationEntry[] Matched { get; set; } = Array.Empty<ReconciliationEntry>();
    public ReconciliationEntry[] ProviderOnly { get; set; } = Array.Empty<ReconciliationEntry>();
    public ReconciliationEntry[] LocalOnly { get; set; } = Array.Empty<ReconciliationEntry>();
}
