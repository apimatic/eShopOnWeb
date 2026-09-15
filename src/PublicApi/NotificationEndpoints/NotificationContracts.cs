using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>Request to re-send a message. Carries the caller-supplied idempotency key.</summary>
public class ResendNotificationRequest
{
    /// <summary>
    /// A caller-supplied key. Repeating a request under the same key returns the message the first attempt
    /// produced without sending another; a genuine second attempt uses a fresh key.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

/// <summary>Response to a resend. Returns the identifier of the message the resend produced as a top-level field.</summary>
public class ResendNotificationResponse
{
    public int NotificationId { get; set; }

    /// <summary>True when this idempotency key had already been used, so no new message was sent.</summary>
    public bool AlreadyProcessed { get; set; }
}

/// <summary>One reconciled message in the report.</summary>
public class ReconciliationEntryDto
{
    public string? ProviderMessageSid { get; set; }

    /// <summary>Matched / ProviderOnly / EShopOnly.</summary>
    public string Match { get; set; } = string.Empty;

    public string? ProviderStatus { get; set; }
    public int? ProviderErrorCode { get; set; }
    public int? NotificationId { get; set; }
    public int? OrderId { get; set; }
    public string? EShopStatus { get; set; }
    public string? MaskedTo { get; set; }
    public DateTimeOffset? DateSent { get; set; }

    public static ReconciliationEntryDto FromEntry(ReconciliationEntry e) => new()
    {
        ProviderMessageSid = e.ProviderMessageSid,
        Match = e.Match.ToString(),
        ProviderStatus = e.ProviderStatus,
        ProviderErrorCode = e.ProviderErrorCode,
        NotificationId = e.NotificationId,
        OrderId = e.OrderId,
        EShopStatus = e.EShopStatus,
        MaskedTo = e.MaskedTo,
        DateSent = e.DateSent
    };
}

/// <summary>The reconciliation report over a date range.</summary>
public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int ProviderMessageCount { get; set; }
    public int EShopMessageCount { get; set; }
    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int EShopOnlyCount { get; set; }
    public List<ReconciliationEntryDto> Entries { get; set; } = new();
}
