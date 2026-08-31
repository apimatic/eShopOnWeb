using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

public class ReconciliationReport
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public int ProviderMessageCount { get; set; }
    public int LocalNotificationCount { get; set; }

    /// <summary>Messages both sides agree on.</summary>
    public List<ReconciliationMatch> Matched { get; set; } = new();

    /// <summary>Messages the provider knows about from our sending number that eShop has no record of.</summary>
    public List<ReconciliationProviderMessage> MissingFromLocal { get; set; } = new();

    /// <summary>Messages eShop believes it sent that the provider no longer reports in the range.</summary>
    public List<ReconciliationLocalNotification> MissingFromProvider { get; set; } = new();
}

public class ReconciliationMatch
{
    public string MessageSid { get; set; } = string.Empty;
    public int NotificationId { get; set; }
    public string ProviderStatus { get; set; } = string.Empty;
    public string LocalStatus { get; set; } = string.Empty;
}

public class ReconciliationProviderMessage
{
    public string MessageSid { get; set; } = string.Empty;
    public string? To { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? DateSent { get; set; }
    public DateTimeOffset? DateCreated { get; set; }
}

public class ReconciliationLocalNotification
{
    public int NotificationId { get; set; }
    public string? MessageSid { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
