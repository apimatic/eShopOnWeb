using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>
/// A report that lines the provider's own record of messages (for eShop's configured sending
/// number, over a date range) up against what eShop believes it sent, so that a message the
/// provider knows about and eShop does not — or the reverse — is visible.
/// </summary>
public class ReconciliationReport
{
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }

    /// <summary>Messages present in both the provider's records and eShop's, matched by SID.</summary>
    public IReadOnlyList<ReconciliationMatch> Matched { get; init; } = new List<ReconciliationMatch>();

    /// <summary>Messages the provider knows about that eShop has no record of.</summary>
    public IReadOnlyList<ReconciliationProviderOnly> ProviderOnly { get; init; } = new List<ReconciliationProviderOnly>();

    /// <summary>Messages eShop believes it sent that the provider has no record of in range.</summary>
    public IReadOnlyList<ReconciliationEShopOnly> EShopOnly { get; init; } = new List<ReconciliationEShopOnly>();

    public int ProviderMessageCount { get; init; }
    public int EShopMessageCount { get; init; }
}

/// <summary>A message present on both sides, with each side's status for comparison.</summary>
public class ReconciliationMatch
{
    public required string MessageSid { get; init; }
    public int NotificationId { get; init; }
    public int OrderId { get; init; }
    public string? ProviderStatus { get; init; }
    public string? EShopStatus { get; init; }
    public bool StatusesAgree { get; init; }
}

/// <summary>A message the provider recorded that eShop has no notification for.</summary>
public class ReconciliationProviderOnly
{
    public required string MessageSid { get; init; }
    public string? ProviderStatus { get; init; }
    public string? DateSent { get; init; }

    /// <summary>Destination masked to its last four digits — the full number is never surfaced.</summary>
    public string? MaskedTo { get; init; }
}

/// <summary>A message eShop believes it sent that the provider has no record of in range.</summary>
public class ReconciliationEShopOnly
{
    public required string MessageSid { get; init; }
    public int NotificationId { get; init; }
    public int OrderId { get; init; }
    public string? EShopStatus { get; init; }
}
