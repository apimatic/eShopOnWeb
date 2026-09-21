using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>Body of <c>POST /api/notifications/{id}/resend</c>: the caller-supplied idempotency key.</summary>
public class ResendNotificationRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
}

/// <summary>Response of resend. Returns the identifier of the message the resend produced.</summary>
public class ResendNotificationResponse
{
    public int NotificationId { get; set; }
}

public class ReconciliationEntryDto
{
    public string? ProviderMessageSid { get; set; }
    public int? NotificationId { get; set; }
    public int? OrderId { get; set; }
    public string? ProviderStatus { get; set; }
    public string? EShopState { get; set; }
    public DateTimeOffset? ProviderDateSent { get; set; }
}

/// <summary>
/// Reconciliation report over a date range, lining up the provider's own record against what eShop believes
/// it sent. <see cref="ProviderOnly"/> = the provider knows but eShop does not; <see cref="EShopOnly"/> = the
/// reverse (within the window); <see cref="OutOfWindow"/> = local records whose send time falls outside the
/// range — not a discrepancy.
/// </summary>
public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public bool Truncated { get; set; }
    public List<ReconciliationEntryDto> Matched { get; set; } = new();
    public List<ReconciliationEntryDto> ProviderOnly { get; set; } = new();
    public List<ReconciliationEntryDto> EShopOnly { get; set; } = new();
    public List<ReconciliationEntryDto> OutOfWindow { get; set; } = new();
}
