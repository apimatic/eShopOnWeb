using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

public record ReconciliationEntry(
    string? ProviderMessageSid,
    int? NotificationId,
    string? ProviderStatus,
    string? LocalStatus,
    string? To,
    string? DateSent);

public record ReconciliationReport(
    string From,
    string To,
    int ProviderMessageCount,
    int LocalNotificationCount,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> LocalOnly);
