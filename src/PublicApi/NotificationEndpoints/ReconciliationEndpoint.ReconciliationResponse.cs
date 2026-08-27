using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int ProviderMessageCount { get; set; }
    public int LocalNotificationCount { get; set; }

    /// <summary>Messages present in both the provider's records and eShop's.</summary>
    public List<ReconciliationMatch> Matched { get; set; } = new();

    /// <summary>Messages the provider knows about from our sending number that eShop has no record of.</summary>
    public List<ReconciliationProviderMessage> ProviderOnly { get; set; } = new();

    /// <summary>Notifications eShop believes it sent but the provider has no record of.</summary>
    public List<ReconciliationLocalNotification> LocalOnly { get; set; } = new();

    /// <summary>Local notifications that were never accepted by the provider.</summary>
    public List<int> NotAcceptedByProvider { get; set; } = new();
}

public class ReconciliationMatch
{
    public string ProviderMessageSid { get; set; } = string.Empty;
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string? ProviderStatus { get; set; }
    public string? LocalStatus { get; set; }
}

public class ReconciliationProviderMessage
{
    public string ProviderMessageSid { get; set; } = string.Empty;
    public string? Status { get; set; }
    public DateTimeOffset? DateSent { get; set; }
}

public class ReconciliationLocalNotification
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string? LocalStatus { get; set; }
}
