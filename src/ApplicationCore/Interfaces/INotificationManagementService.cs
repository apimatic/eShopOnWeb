using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Operator actions on notifications: re-send, content disposal, reconciliation.</summary>
public interface INotificationManagementService
{
    /// <summary>
    /// Re-sends a message that did not reach the shopper. Repeating the request under the same
    /// idempotency key returns the notification the first attempt produced without sending again.
    /// </summary>
    Task<Notification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Disposes of the message text at the provider and locally; the record and outcome survive.</summary>
    Task DisposeContentAsync(int notificationId, CancellationToken ct = default);

    /// <summary>
    /// Lines up the provider's own record of messages for the range against what eShop believes it sent.
    /// </summary>
    Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}

public class NotificationReconciliationReport
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<NotificationReconciliationEntry> Entries { get; set; } = new();
    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int LocalOnlyCount { get; set; }

    /// <summary>True when the provider listing was truncated by a page cap and the report may be incomplete.</summary>
    public bool ProviderListingTruncated { get; set; }
}

public class NotificationReconciliationEntry
{
    public string? MessageSid { get; set; }
    public int? NotificationId { get; set; }

    /// <summary>Matched | ProviderOnly | LocalOnly</summary>
    public string Match { get; set; } = string.Empty;
    public string? ProviderStatus { get; set; }
    public string? LocalStatus { get; set; }
    public DateTimeOffset? DateSent { get; set; }
}
