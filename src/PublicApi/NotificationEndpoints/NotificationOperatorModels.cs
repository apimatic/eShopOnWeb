using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationBody
{
    /// <summary>Caller-supplied idempotency key: a repeat under the same key sends no second message.</summary>
    [Required]
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationRequest
{
    [FromRoute(Name = "notificationId")]
    public int NotificationId { get; set; }

    [FromBody]
    public ResendNotificationBody Payload { get; set; } = new();
}

public class ResendNotificationResponse
{
    /// <summary>The identifier of the message the resend produced (top-level).</summary>
    public int NotificationId { get; set; }
}

public class ReconciliationEntryDto
{
    public string? ProviderSid { get; set; }
    public string? ProviderStatus { get; set; }
    public int? NotificationId { get; set; }
    public string? LocalState { get; set; }
    public DateTimeOffset? ProviderDateSent { get; set; }
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public bool Truncated { get; set; }
    public int ProviderPagesFetched { get; set; }
    public List<ReconciliationEntryDto> Matched { get; set; } = new();
    public List<ReconciliationEntryDto> InProviderOnly { get; set; } = new();
    public List<ReconciliationEntryDto> InEShopOnly { get; set; } = new();
}
