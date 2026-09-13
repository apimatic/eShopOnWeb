using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioSubscriptionService
{
    Task<IReadOnlyList<MaxioProductResult>> ListPlansAsync(CancellationToken ct);
    Task<MaxioSubscriptionResult> SubscribeAsync(string email, string firstName, string lastName, string productHandle, string? reference, CancellationToken ct);
    Task<IReadOnlyList<MaxioSubscriptionResult>> ListMySubscriptionsAsync(string email, CancellationToken ct);
}

public class MaxioProductResult
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
}

public class MaxioSubscriptionResult
{
    public int Id { get; set; }
    public string? State { get; set; }
    public string? ProductName { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
}
