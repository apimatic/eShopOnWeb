using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>Body of a resend request. The idempotency key is supplied by the caller.</summary>
public class ResendNotificationRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
}

/// <summary>Response to a resend. Returns the identifier of the message the resend produced.</summary>
public class ResendNotificationResponse
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string DeliveryStatus { get; set; } = string.Empty;
    public string? MessageSid { get; set; }
}

public class ReconciliationEntryDto
{
    public string? MessageSid { get; set; }
    public int? NotificationId { get; set; }
    public int? OrderId { get; set; }
    public string? ProviderStatus { get; set; }
    public string? LocalStatus { get; set; }
    public DateTimeOffset? DateSent { get; set; }

    public static ReconciliationEntryDto From(ReconciliationEntry e) => new()
    {
        MessageSid = e.MessageSid,
        NotificationId = e.NotificationId,
        OrderId = e.OrderId,
        ProviderStatus = e.ProviderStatus,
        LocalStatus = e.LocalStatus,
        DateSent = e.DateSent
    };
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string SendingNumber { get; set; } = string.Empty;

    /// <summary>Messages the provider reports and eShop also has a record of.</summary>
    public List<ReconciliationEntryDto> Matched { get; set; } = new();

    /// <summary>Messages the provider knows about that eShop has no record of.</summary>
    public List<ReconciliationEntryDto> ProviderOnly { get; set; } = new();

    /// <summary>Messages eShop believes it sent that the provider's record for the range does not include.</summary>
    public List<ReconciliationEntryDto> EShopOnly { get; set; } = new();

    public int MatchedCount => Matched.Count;
    public int ProviderOnlyCount => ProviderOnly.Count;
    public int EShopOnlyCount => EShopOnly.Count;
}
