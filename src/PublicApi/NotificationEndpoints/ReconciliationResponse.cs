using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }

    public ReconciliationRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ReconciliationResponse()
    {
    }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public List<ReconciledNotificationDto> Matches { get; set; } = new();
    public List<ProviderOnlyMessageDto> ProviderOnly { get; set; } = new();
    public List<ReconciledNotificationDto> EShopOnly { get; set; } = new();
}

public class ReconciledNotificationDto
{
    public int NotificationId { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string? Kind { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class ProviderOnlyMessageDto
{
    public string? ProviderMessageSid { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset? DateCreated { get; set; }
    public DateTimeOffset? DateSent { get; set; }
}
