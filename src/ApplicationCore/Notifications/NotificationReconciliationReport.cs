using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>
/// Lines the provider's own record of messages (for this application's sending number, over a date
/// range) up against what this application believes it sent, so any message one side knows about and
/// the other doesn't is visible.
/// </summary>
public record NotificationReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciledMessage> Matched,
    IReadOnlyList<ProviderOnlyMessage> ProviderOnly,
    IReadOnlyList<EShopOnlyMessage> EShopOnly)
{
    public int ProviderMessageCount => Matched.Count + ProviderOnly.Count;
    public int EShopMessageCount => Matched.Count + EShopOnly.Count;
    public int MatchedCount => Matched.Count;
    public int ProviderOnlyCount => ProviderOnly.Count;
    public int EShopOnlyCount => EShopOnly.Count;
}

/// <summary>A message present on both sides, matched by provider identifier.</summary>
public record ReconciledMessage(int NotificationId, string Sid, string EShopStatus, string ProviderStatus);

/// <summary>A message the provider has for our sending number that this application has no record of.</summary>
public record ProviderOnlyMessage(string Sid, string ProviderStatus, DateTimeOffset? DateSent);

/// <summary>A message this application recorded sending that the provider did not return for the range.</summary>
public record EShopOnlyMessage(int NotificationId, string Sid, string EShopStatus);
