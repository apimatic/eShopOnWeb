using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconcileNotificationsRequest : BaseRequest
{
    /// <summary>Range start (ISO-8601), from the query string.</summary>
    public DateTimeOffset From { get; set; }

    /// <summary>Range end (ISO-8601), from the query string.</summary>
    public DateTimeOffset To { get; set; }
}

public class ReconcileNotificationsResponse : BaseResponse
{
    public ReconcileNotificationsResponse(Guid correlationId) : base(correlationId) {}
    public ReconcileNotificationsResponse() {}

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }

    /// <summary>The application's configured sending number the provider side was queried for.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>True when the provider list hit the page cap and the range may be incomplete.</summary>
    public bool ProviderListTruncated { get; set; }

    /// <summary>Messages both sides know about.</summary>
    public List<ReconciledNotificationDto> Matched { get; set; } = new();

    /// <summary>Messages the provider knows about from this sending number and eShop does not.</summary>
    public List<ProviderMessageDto> ProviderOnly { get; set; } = new();

    /// <summary>Messages eShop believes it sent that the provider list does not show.</summary>
    public List<AppOnlyNotificationDto> AppOnly { get; set; } = new();
}

public class ReconciledNotificationDto
{
    public int NotificationId { get; set; }
    public string MessageSid { get; set; } = string.Empty;
    public string? AppStatus { get; set; }
    public string? ProviderStatus { get; set; }
    public bool StatusMatches { get; set; }
}

public class ProviderMessageDto
{
    public string MessageSid { get; set; } = string.Empty;
    public string? To { get; set; }
    public string? From { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset? DateSent { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}

public class AppOnlyNotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? MessageSid { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
