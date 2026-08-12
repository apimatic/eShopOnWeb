using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Lines up the provider's own record of messages (from the configured sending number, over a date
/// range) against what the shop believes it sent, so a message one side knows about and the other
/// doesn't is visible.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int ProviderMessageCount,
    int EShopNotificationCount,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<ProviderOnlyEntry> ProviderOnly,
    IReadOnlyList<EShopOnlyEntry> EShopOnly);

/// <summary>A message both sides agree on, with each side's view of its status.</summary>
public record ReconciliationMatch(
    string Sid,
    int NotificationId,
    int OrderId,
    NotificationKind Kind,
    string ProviderStatus,
    string EShopStatus);

/// <summary>A message the provider reports that the shop has no notification for.</summary>
public record ProviderOnlyEntry(string Sid, string ProviderStatus, string? DateSent);

/// <summary>A notification the shop holds that the provider did not report in the range.</summary>
public record EShopOnlyEntry(int NotificationId, int OrderId, string? Sid, NotificationKind Kind, string EShopStatus, string Reason);
