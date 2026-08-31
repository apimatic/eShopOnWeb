using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Messaging;

public enum ReconciliationDisposition
{
    /// <summary>Present in both the provider's records and eShop's.</summary>
    Matched = 0,

    /// <summary>The provider knows about this message; eShop has no record of it.</summary>
    MissingLocally = 1,

    /// <summary>eShop believes it sent this message; the provider has no record of it.</summary>
    MissingAtProvider = 2
}

public record ReconciliationEntry(
    string? ProviderMessageSid,
    int? LocalNotificationId,
    int? LocalOrderId,
    string? To,
    string? ProviderStatus,
    string? LocalStatus,
    DateTimeOffset? DateSent,
    ReconciliationDisposition Disposition);

public record NotificationReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int ProviderMessageCount,
    int LocalNotificationCount,
    IReadOnlyList<ReconciliationEntry> Entries);
