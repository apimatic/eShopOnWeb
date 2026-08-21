using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconcileNotificationsRequest
{
    public ReconcileNotificationsRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }

    public DateTimeOffset From { get; }
    public DateTimeOffset To { get; }
}

public class ReconcileNotificationsResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public List<ReconciledNotificationDto> Matched { get; set; } = new();
    public List<ProviderOnlyMessageDto> ProviderOnly { get; set; } = new();
    public List<NotificationDto> ApplicationOnly { get; set; } = new();
}

public class ReconciledNotificationDto
{
    public int NotificationId { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string ApplicationStatus { get; set; } = string.Empty;
    public string ProviderStatus { get; set; } = string.Empty;
    public string? ProviderErrorCode { get; set; }
}

public class ProviderOnlyMessageDto
{
    public string ProviderMessageSid { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? DateSent { get; set; }
    public DateTimeOffset? DateCreated { get; set; }
}
