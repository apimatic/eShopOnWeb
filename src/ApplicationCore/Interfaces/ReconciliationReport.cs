using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Lines up the provider's own record of messages (sent from this app's configured sending number) over a
/// date range against what eShop believes it sent, so a message one side knows about and the other does not
/// is visible.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int ProviderMessageCount,
    int EShopNotificationCount,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<ReconciliationEntry> OnlyAtProvider,
    IReadOnlyList<ReconciliationEntry> OnlyInEShop);

/// <summary>A message present on both sides, matched by the provider's message identifier.</summary>
public record ReconciliationMatch(
    string ProviderMessageSid,
    string? ProviderStatus,
    int NotificationId,
    int OrderId,
    string? EShopStatus);

/// <summary>A message known to only one side.</summary>
public record ReconciliationEntry(
    string? ProviderMessageSid,
    string? ProviderStatus,
    int? NotificationId,
    int? OrderId,
    DateTimeOffset? SentAt);
