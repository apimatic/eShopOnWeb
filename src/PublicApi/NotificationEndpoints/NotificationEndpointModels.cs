using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.PublicApi.SmsNotifications;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>Re-send a message that did not reach the shopper. Carries a caller-supplied idempotency key
/// (which may also be supplied via the <c>Idempotency-Key</c> header).</summary>
public class ResendNotificationRequest : BaseRequest
{
    public string? IdempotencyKey { get; set; }
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }
    public ResendNotificationResponse() { }

    /// <summary>Identifier of the message the resend produced.</summary>
    public int NotificationId { get; set; }

    public NotificationDto Notification { get; set; } = new();
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }

    /// <summary>The application's configured sending number the report was scoped to.</summary>
    public string FromNumber { get; set; } = string.Empty;

    public List<ReconciliationEntryDto> Matched { get; set; } = new();

    /// <summary>Messages the provider has a record of that eShop does not.</summary>
    public List<ReconciliationEntryDto> ProviderOnly { get; set; } = new();

    /// <summary>Messages eShop has a record of that the provider does not.</summary>
    public List<ReconciliationEntryDto> EShopOnly { get; set; } = new();

    public static ReconciliationResponse FromReport(ReconciliationReport report) => new()
    {
        From = report.From,
        To = report.To,
        FromNumber = report.FromNumber,
        Matched = report.Matched.Select(ReconciliationEntryDto.From).ToList(),
        ProviderOnly = report.ProviderOnly.Select(ReconciliationEntryDto.From).ToList(),
        EShopOnly = report.EShopOnly.Select(ReconciliationEntryDto.From).ToList()
    };
}

public class ReconciliationEntryDto
{
    public string? ProviderSid { get; set; }
    public int? NotificationId { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset? SentAt { get; set; }

    public static ReconciliationEntryDto From(ReconciliationEntry entry) => new()
    {
        ProviderSid = entry.ProviderSid,
        NotificationId = entry.NotificationId,
        Status = entry.Status,
        SentAt = entry.SentAt
    };
}
