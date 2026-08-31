using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) {}
    public ReconciliationResponse() {}

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int ProviderMessageCount { get; set; }
    public int EshopNotificationCount { get; set; }

    /// <summary>Messages both sides know about.</summary>
    public List<ReconciledNotificationDto> Matched { get; set; } = new();

    /// <summary>Messages the provider records from our sending number that eShop has no record of.</summary>
    public List<ProviderOnlyMessageDto> OnlyAtProvider { get; set; } = new();

    /// <summary>Notifications eShop believes it sent that the provider has no record of in range.</summary>
    public List<EshopOnlyNotificationDto> OnlyInEshop { get; set; } = new();
}

public class ReconciledNotificationDto
{
    public int NotificationId { get; set; }
    public string MessageSid { get; set; } = string.Empty;
    public string? ProviderStatus { get; set; }
    public string RecordedStatus { get; set; } = string.Empty;
}

public class ProviderOnlyMessageDto
{
    public string MessageSid { get; set; } = string.Empty;
    public string? To { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset? DateSent { get; set; }
}

public class EshopOnlyNotificationDto
{
    public int NotificationId { get; set; }
    public string MessageSid { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}
