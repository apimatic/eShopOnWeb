using System;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// A Maxio Advanced Billing subscription.
/// </summary>
public class MaxioSubscription
{
    public long Id { get; init; }
    public long CustomerId { get; init; }
    public string State { get; init; } = string.Empty;
    public string? ProductHandle { get; init; }
    public string? ProductName { get; init; }
    public long PriceInCents { get; init; }
    public DateTimeOffset? NextAssessmentAt { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
