using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.Services;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class GetReconciliationRequest : CancellableRequest
{
    public GetReconciliationRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }

    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
}

public class GetReconciliationResponse : BaseResponse
{
    public GetReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public GetReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }

    /// <summary>This application's configured sending number — the only sender the report counts.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>True when the provider had more pages than the report's page cap.</summary>
    public bool Truncated { get; set; }

    public int ProviderMessageCount { get; set; }
    public int AppNotificationCount { get; set; }

    /// <summary>Messages both sides agree on, with whether the two states agree.</summary>
    public IReadOnlyList<ReconciliationMatch> Matched { get; set; } = Array.Empty<ReconciliationMatch>();

    /// <summary>Messages the provider knows about from this sender that eShop has no record of.</summary>
    public IReadOnlyList<ReconciliationProviderEntry> ProviderOnly { get; set; } = Array.Empty<ReconciliationProviderEntry>();

    /// <summary>Notifications eShop recorded that the provider has no matching message for.</summary>
    public IReadOnlyList<ReconciliationAppEntry> AppOnly { get; set; } = Array.Empty<ReconciliationAppEntry>();

    public string? Error { get; set; }
}
