using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

public record ReconciliationEntry(
    int? NotificationId,
    string? MessageSid,
    string? To,
    string? Status,
    System.DateTimeOffset? Date);

/// <summary>
/// Lines up the provider's own record of messages against what eShop believes it sent.
/// ProviderOnly = the provider knows about it and eShop doesn't; LocalOnly = the reverse.
/// </summary>
public record ReconciliationReport(
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> ProviderOnly,
    IReadOnlyList<ReconciliationEntry> LocalOnly);
