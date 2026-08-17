using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendRequest : BaseRequest
{
    /// <summary>Set from the route.</summary>
    public int NotificationId { get; set; }

    /// <summary>Caller-supplied idempotency key: a repeat under the same key sends nothing new.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendResponse : BaseResponse
{
    public ResendResponse(Guid correlationId) : base(correlationId) { }
    public ResendResponse() { }

    /// <summary>The identifier of the message the resend produced (or replayed).</summary>
    public int NotificationId { get; set; }
    public string Outcome { get; set; } = string.Empty;
}

public class RedactContentRequest : BaseRequest
{
    public RedactContentRequest(int notificationId) => NotificationId = notificationId;
    public int NotificationId { get; set; }
}

public class ReconciliationRequest : BaseRequest
{
    public ReconciliationRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconciliationEntryDto
{
    public string ProviderMessageId { get; set; } = string.Empty;
    public string? To { get; set; }
    public string? ProviderStatus { get; set; }
    public int? ErrorCode { get; set; }
    public DateTimeOffset? DateSent { get; set; }
    public int? NotificationId { get; set; }
    public string? EShopStatus { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public int ProviderCount { get; set; }
    public int EShopCount { get; set; }

    /// <summary>Messages both the provider and eShop agree on.</summary>
    public List<ReconciliationEntryDto> Matched { get; set; } = new();

    /// <summary>Messages the provider knows about that eShop does not.</summary>
    public List<ReconciliationEntryDto> ProviderOnly { get; set; } = new();

    /// <summary>Messages eShop believes it sent that the provider's record does not show.</summary>
    public List<ReconciliationEntryDto> EShopOnly { get; set; } = new();
}
