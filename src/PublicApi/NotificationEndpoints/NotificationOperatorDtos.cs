using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest : BaseRequest
{
    /// <summary>The notification to re-send (from the route).</summary>
    public int NotificationId { get; set; }

    /// <summary>
    /// Caller-supplied idempotency key. A repeat under the same key does not send a second message;
    /// a fresh key is a new, legitimate attempt.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }
    public ResendNotificationResponse() { }

    /// <summary>Identifier of the message the re-send produced.</summary>
    public int NotificationId { get; set; }

    public string Status { get; set; } = string.Empty;
}

public class DisposeNotificationContentRequest : BaseRequest
{
    public int NotificationId { get; init; }
    public DisposeNotificationContentRequest(int notificationId) => NotificationId = notificationId;
}

public class DisposeNotificationContentResponse : BaseResponse
{
    public DisposeNotificationContentResponse(Guid correlationId) : base(correlationId) { }
    public DisposeNotificationContentResponse() { }

    public string Status { get; set; } = "ContentDisposed";
}

public class ReconciliationRequest : BaseRequest
{
    public string? From { get; init; }
    public string? To { get; init; }

    public ReconciliationRequest(string? from, string? to)
    {
        From = from;
        To = to;
    }
}

public class ReconciliationMatchDto
{
    public string ProviderMessageId { get; set; } = string.Empty;
    public string ProviderStatus { get; set; } = string.Empty;
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string EShopStatus { get; set; } = string.Empty;
}

public class ProviderOnlyMessageDto
{
    public string ProviderMessageId { get; set; } = string.Empty;
    public string ProviderStatus { get; set; } = string.Empty;
    public DateTimeOffset? DateSent { get; set; }
}

public class EShopOnlyNotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string? ProviderMessageId { get; set; }
    public string EShopStatus { get; set; } = string.Empty;
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }

    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int EShopOnlyCount { get; set; }

    /// <summary>Messages the provider and eShop agree on.</summary>
    public List<ReconciliationMatchDto> Matched { get; set; } = new();

    /// <summary>Messages the provider knows about that eShop does not.</summary>
    public List<ProviderOnlyMessageDto> ProviderOnly { get; set; } = new();

    /// <summary>Notifications eShop believes it sent that the provider's record does not include.</summary>
    public List<EShopOnlyNotificationDto> EShopOnly { get; set; } = new();
}
