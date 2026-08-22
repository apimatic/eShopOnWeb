using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Messaging;

public record ReconciliationEntry(
    int? NotificationId,
    string? ProviderMessageSid,
    string? Status,
    DateTimeOffset? DateSent,
    string Match);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> InProviderOnly,
    IReadOnlyList<ReconciliationEntry> InEShopOnly);
