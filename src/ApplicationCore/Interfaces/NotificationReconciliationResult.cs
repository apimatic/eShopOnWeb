using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class NotificationReconciliationResult
{
    public NotificationReconciliationResult(
        DateTimeOffset from,
        DateTimeOffset to,
        string fromNumber,
        IReadOnlyList<ReconciliationEntry> matched,
        IReadOnlyList<ReconciliationEntry> providerOnly,
        IReadOnlyList<ReconciliationEntry> applicationOnly)
    {
        From = from;
        To = to;
        FromNumber = fromNumber;
        Matched = matched;
        ProviderOnly = providerOnly;
        ApplicationOnly = applicationOnly;
    }

    public DateTimeOffset From { get; }
    public DateTimeOffset To { get; }
    public string FromNumber { get; }
    public IReadOnlyList<ReconciliationEntry> Matched { get; }
    public IReadOnlyList<ReconciliationEntry> ProviderOnly { get; }
    public IReadOnlyList<ReconciliationEntry> ApplicationOnly { get; }
}

public class ReconciliationEntry
{
    public string? ProviderMessageSid { get; init; }
    public string? ProviderStatus { get; init; }
    public string? DateSent { get; init; }
    public int? NotificationId { get; init; }
    public NotificationKind? Kind { get; init; }
    public int? OrderId { get; init; }
}
