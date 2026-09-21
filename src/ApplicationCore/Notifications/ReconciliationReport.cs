using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>One message lined up across the provider's record and eShop's record.</summary>
public record ReconciliationEntry(
    string Sid,
    string? ProviderStatus,
    string? LocalStatus,
    int? NotificationId,
    DateTimeOffset? DateSent);

/// <summary>
/// The provider's own record of messages for a range lined up against what eShop believes it sent, so a
/// message the provider knows about and eShop doesn't — or the reverse — is visible. Filtered to this
/// application's configured sending number, over the whole range.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int ProviderCount,
    int LocalCount,
    int MatchedCount,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> EShopOnly);
