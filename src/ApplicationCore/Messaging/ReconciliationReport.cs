using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

/// <summary>
/// Lines up the provider's own record of messages for a date range against
/// what eShop believes it sent.
/// </summary>
public class ReconciliationReport
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Every message the provider recorded for the sending number in the range.</summary>
    public List<ReconciledProviderMessage> ProviderMessages { get; set; } = new();

    /// <summary>Messages eShop recorded in the range that the provider has no record of.</summary>
    public List<UnmatchedLocalNotification> LocalOnlyNotifications { get; set; } = new();

    public int ProviderMessageCount => ProviderMessages.Count;
    public int MatchedCount => ProviderMessages.FindAll(m => m.MatchedNotificationId.HasValue).Count;
    public int ProviderOnlyCount => ProviderMessages.Count - MatchedCount;
    public int LocalOnlyCount => LocalOnlyNotifications.Count;
}

public class ReconciledProviderMessage
{
    public string MessageSid { get; set; } = string.Empty;
    public string? To { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? DateSent { get; set; }
    public int? ErrorCode { get; set; }

    /// <summary>The local notification this provider message lines up with, if any.</summary>
    public int? MatchedNotificationId { get; set; }
    public int? OrderId { get; set; }
    public string? NotificationType { get; set; }

    /// <summary>"Matched" when eShop has a record of this message, "ProviderOnly" otherwise.</summary>
    public string Match => MatchedNotificationId.HasValue ? "Matched" : "ProviderOnly";
}

public class UnmatchedLocalNotification
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string Status { get; set; } = string.Empty;
    public string NotificationType { get; set; } = string.Empty;
    public string Match => "LocalOnly";
}
