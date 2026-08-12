using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// A reconciliation of the provider's own record of sent messages against what eShop believes it
/// sent, over a date range, for this application's configured sending number.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    int ProviderCount,
    int EShopCount,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> EShopOnly);

/// <summary>
/// One line of a reconciliation. A provider-only line is a message the provider knows about that
/// eShop has no record of; an eShop-only line is one eShop believes it sent that the provider's
/// range query did not return.
/// </summary>
public record ReconciliationEntry(
    string? ProviderMessageSid,
    int? NotificationId,
    int? OrderId,
    string? ProviderStatus,
    string? EShopStatus,
    DateTimeOffset? DateSent);
