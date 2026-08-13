using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

/// <summary>
/// One message lined up between what the provider records and what eShop believes it sent, keyed by
/// the provider message identifier. A field is null on the side that has no record of the message.
/// </summary>
public record ReconciliationEntry(
    string ProviderSid,
    string? ProviderStatus,
    string? EShopStatus,
    int? NotificationId);

/// <summary>
/// A reconciliation over a date range: messages present in both records, only at the provider, and
/// only in eShop.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> EShopOnly);
