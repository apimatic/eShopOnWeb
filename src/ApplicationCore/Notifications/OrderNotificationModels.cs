using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>One line of a new order: a catalog item and how many of it.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>
/// One message as it appears in a reconciliation run: whether the provider knows it, whether eShop
/// knows it, and their respective outcomes. Carries no phone number.
/// </summary>
public record ReconciliationEntry(
    string MessageSid,
    bool InProvider,
    bool InEShop,
    string? ProviderStatus,
    string? EShopStatus,
    int? NotificationId,
    int? OrderId,
    DateTimeOffset? DateSentUtc);

/// <summary>
/// A reconciliation report over a date range: the provider's own record of this application's messages
/// lined up against what eShop believes it sent, so either-side-only messages are visible.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    int ProviderCount,
    int EShopCount,
    int MatchedCount,
    IReadOnlyList<ReconciliationEntry> OnlyInProvider,
    IReadOnlyList<ReconciliationEntry> OnlyInEShop,
    IReadOnlyList<ReconciliationEntry> Matched,
    bool ProviderResultTruncated);
