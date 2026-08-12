using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>
/// Lines up the provider's own record of messages (for this application's sending number, over a
/// date range) against what eShop believes it sent, so a message the provider knows about and eShop
/// doesn't — or the reverse — is visible.
/// </summary>
public class ReconciliationReport
{
    public DateTimeOffset FromUtc { get; init; }
    public DateTimeOffset ToUtc { get; init; }

    /// <summary>This application's sending number the provider was queried for.</summary>
    public string FromNumber { get; init; } = string.Empty;

    /// <summary>Messages both sides agree on, matched by provider SID.</summary>
    public IReadOnlyList<ReconciliationMatch> Matched { get; init; } = new List<ReconciliationMatch>();

    /// <summary>Messages the provider has a record of that eShop has no notification for.</summary>
    public IReadOnlyList<ProviderMessageRecord> OnlyAtProvider { get; init; } = new List<ProviderMessageRecord>();

    /// <summary>Notifications eShop recorded as sent (with a SID) that the provider did not return for this range.</summary>
    public IReadOnlyList<Notification> OnlyInEShop { get; init; } = new List<Notification>();
}

/// <summary>A single message present on both sides, with each side's view of its delivery outcome.</summary>
public class ReconciliationMatch
{
    public string Sid { get; init; } = string.Empty;
    public int NotificationId { get; init; }
    public string? ProviderStatus { get; init; }
    public string? EShopStatus { get; init; }
    public bool StatusMatches { get; init; }
}
