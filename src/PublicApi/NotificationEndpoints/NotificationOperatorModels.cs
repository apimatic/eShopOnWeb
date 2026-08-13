using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest : BaseRequest
{
    /// <summary>
    /// Caller-supplied idempotency key. Repeating a resend under the same key returns the message the first
    /// attempt produced instead of sending a second one. May also be supplied via the Idempotency-Key header.
    /// </summary>
    public string? IdempotencyKey { get; set; }
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }
    public ResendNotificationResponse() { }

    /// <summary>The identifier of the message this resend produced.</summary>
    public int NotificationId { get; set; }

    /// <summary>True when this was an idempotent replay of a prior resend under the same key (nothing new was sent).</summary>
    public bool Replayed { get; set; }

    public NotificationDto? Notification { get; set; }
}

public class DisposeNotificationContentResponse : BaseResponse
{
    public DisposeNotificationContentResponse(Guid correlationId) : base(correlationId) { }
    public DisposeNotificationContentResponse() { }

    public int NotificationId { get; set; }
    public bool ContentRedacted { get; set; }
}

public class ReconciliationEntryDto
{
    public string? ProviderMessageSid { get; set; }
    public string? ProviderStatus { get; set; }
    public string? EShopStatus { get; set; }
    public int? OrderId { get; set; }
    public string? Kind { get; set; }
    public DateTimeOffset? DateSent { get; set; }

    public static ReconciliationEntryDto From(ReconciliationEntry e) => new()
    {
        ProviderMessageSid = e.ProviderMessageSid,
        ProviderStatus = e.ProviderStatus,
        EShopStatus = e.EShopStatus,
        OrderId = e.OrderId,
        Kind = e.Kind?.ToString(),
        DateSent = e.DateSent,
    };
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }

    /// <summary>The configured sending number the report was scoped to.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messages both the provider and eShop have a record of.</summary>
    public List<ReconciliationEntryDto> Matched { get; set; } = new();

    /// <summary>Messages the provider knows about that eShop does not.</summary>
    public List<ReconciliationEntryDto> ProviderOnly { get; set; } = new();

    /// <summary>Messages eShop believes it sent that the provider's record does not show.</summary>
    public List<ReconciliationEntryDto> EShopOnly { get; set; } = new();

    public int MatchedCount => Matched.Count;
    public int ProviderOnlyCount => ProviderOnly.Count;
    public int EShopOnlyCount => EShopOnly.Count;

    public static ReconciliationResponse Create(ReconciliationReport report, Guid correlationId) => new(correlationId)
    {
        From = report.From,
        To = report.To,
        FromNumber = report.FromNumber,
        Matched = report.Matched.Select(ReconciliationEntryDto.From).ToList(),
        ProviderOnly = report.ProviderOnly.Select(ReconciliationEntryDto.From).ToList(),
        EShopOnly = report.EShopOnly.Select(ReconciliationEntryDto.From).ToList(),
    };
}
