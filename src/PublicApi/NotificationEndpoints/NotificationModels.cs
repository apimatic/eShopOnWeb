using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>Body of a resend request: the caller-supplied idempotency key.</summary>
public class ResendRequestBody
{
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationRequest
{
    public int NotificationId { get; set; }
    public string IdempotencyKey { get; set; }

    public ResendNotificationRequest(int notificationId, string idempotencyKey)
    {
        NotificationId = notificationId;
        IdempotencyKey = idempotencyKey;
    }
}

public class ResendNotificationResponse
{
    /// <summary>Identifier of the notification the resend produced (top-level).</summary>
    public int NotificationId { get; set; }

    public string Status { get; set; } = string.Empty;
    public string? MessageSid { get; set; }
}

public class RedactContentRequest
{
    public int NotificationId { get; set; }

    public RedactContentRequest(int notificationId) => NotificationId = notificationId;
}

public class ReconciliationRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }

    public ReconciliationRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }

    /// <summary>The configured sending number whose traffic this report covers.</summary>
    public string FromNumber { get; set; } = string.Empty;

    public int MatchedCount { get; set; }
    public int InEShopOnlyCount { get; set; }
    public int InProviderOnlyCount { get; set; }

    /// <summary>Messages present both at the provider and in eShop.</summary>
    public List<ReconciliationMatchDto> Matched { get; set; } = new();

    /// <summary>Messages eShop believes it sent that the provider's range did not return.</summary>
    public List<ReconciliationEShopEntryDto> InEShopOnly { get; set; } = new();

    /// <summary>Messages the provider reports from this number that eShop has no record of.</summary>
    public List<ReconciliationProviderEntryDto> InProviderOnly { get; set; } = new();
}

public class ReconciliationMatchDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Sid { get; set; } = string.Empty;
    public string EShopStatus { get; set; } = string.Empty;
    public string ProviderStatus { get; set; } = string.Empty;
    public int? ProviderErrorCode { get; set; }
}

public class ReconciliationEShopEntryDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Sid { get; set; } = string.Empty;
    public string EShopStatus { get; set; } = string.Empty;
}

public class ReconciliationProviderEntryDto
{
    public string Sid { get; set; } = string.Empty;
    public string ProviderStatus { get; set; } = string.Empty;
    public int? ProviderErrorCode { get; set; }
    public DateTimeOffset? DateSent { get; set; }
}
