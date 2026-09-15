using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconcileNotificationsResponse : BaseResponse
{
    public ReconcileNotificationsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ReconcileNotificationsResponse()
    {
    }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciledNotificationDto> Matched { get; set; } = new();
    public List<ReconciledNotificationDto> ProviderOnly { get; set; } = new();
    public List<ReconciledNotificationDto> ApplicationOnly { get; set; } = new();
}

public class ReconciledNotificationDto
{
    public int? NotificationId { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string? Status { get; set; }
    public string? Body { get; set; }
    public string? DateSent { get; set; }
    public string? DateCreated { get; set; }
}
