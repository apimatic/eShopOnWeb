using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>Response of POST /api/notifications/{id}/resend — the id of the message the resend produced.</summary>
public class ResendResponse
{
    public int NotificationId { get; set; }

    /// <summary>True when this request repeated a prior idempotency key and no new message was sent.</summary>
    public bool Duplicate { get; set; }
}

public class ReconciliationEntryView
{
    public string? MessageSid { get; set; }
    public string? ProviderStatus { get; set; }
    public string? EShopStatus { get; set; }
    public int? NotificationId { get; set; }
    public string Match { get; set; } = string.Empty;

    public static ReconciliationEntryView From(ReconciliationEntry e) => new()
    {
        MessageSid = e.MessageSid,
        ProviderStatus = e.ProviderStatus,
        EShopStatus = e.EShopStatus,
        NotificationId = e.NotificationId,
        Match = e.Match switch
        {
            ReconciliationMatch.Matched => "matched",
            ReconciliationMatch.OnlyAtProvider => "only_at_provider",
            ReconciliationMatch.OnlyAtEShop => "only_at_eshop",
            _ => "unknown"
        }
    };
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }

    /// <summary>False when a safety page cap was hit before the whole range was walked.</summary>
    public bool Complete { get; set; }
    public int ProviderCount { get; set; }
    public int EShopCount { get; set; }
    public List<ReconciliationEntryView> Entries { get; set; } = new();
}
