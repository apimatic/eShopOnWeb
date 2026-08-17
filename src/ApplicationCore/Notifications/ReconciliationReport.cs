using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>
/// Lines up the provider's own record of messages sent from this application's configured sending
/// number against what eShop believes it sent over the same range, so a message one side knows
/// about and the other does not is visible.
/// </summary>
public class ReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }

    /// <summary>Total messages the provider returned for the sending number in range.</summary>
    public int ProviderMessageCount { get; init; }

    /// <summary>Total eShop notification records (with a provider SID) in range.</summary>
    public int EShopRecordCount { get; init; }

    /// <summary>Messages present on both sides, matched by provider SID.</summary>
    public IReadOnlyList<ReconciliationMatch> Matched { get; init; } = new List<ReconciliationMatch>();

    /// <summary>Messages the provider knows about that eShop has no record of.</summary>
    public IReadOnlyList<ProviderMessageSummary> ProviderOnly { get; init; } = new List<ProviderMessageSummary>();

    /// <summary>eShop records (with a SID) the provider's list for the range did not return.</summary>
    public IReadOnlyList<EShopNotificationSummary> EShopOnly { get; init; } = new List<EShopNotificationSummary>();
}

public class ReconciliationMatch
{
    public string ProviderMessageSid { get; init; } = string.Empty;
    public string? ProviderStatus { get; init; }
    public int NotificationId { get; init; }
    public int OrderId { get; init; }
    public NotificationKind Kind { get; init; }
    public NotificationDeliveryStatus EShopStatus { get; init; }
}

public class ProviderMessageSummary
{
    public string ProviderMessageSid { get; init; } = string.Empty;
    public string? ProviderStatus { get; init; }
    public DateTimeOffset? DateSent { get; init; }
    public int? ErrorCode { get; init; }
}

public class EShopNotificationSummary
{
    public int NotificationId { get; init; }
    public string ProviderMessageSid { get; init; } = string.Empty;
    public int OrderId { get; init; }
    public NotificationKind Kind { get; init; }
    public NotificationDeliveryStatus EShopStatus { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
