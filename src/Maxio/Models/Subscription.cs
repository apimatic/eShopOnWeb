namespace Maxio.Models;

/// <summary>
/// A Maxio subscription. Mirrors the Subscription schema in the Maxio OpenAPI specification.
/// </summary>
public class Subscription
{
    public int Id { get; set; }
    public string? State { get; set; }
    public long BalanceInCents { get; set; }
    public long TotalRevenueInCents { get; set; }
    public long ProductPriceInCents { get; set; }
    public int ProductVersionNumber { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? TrialStartedAt { get; set; }
    public DateTimeOffset? TrialEndedAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public string? CancellationMessage { get; set; }
    public string? CancellationMethod { get; set; }
    public bool? CancelAtEndOfPeriod { get; set; }
    public string? PaymentCollectionMethod { get; set; }
    public string? Reference { get; set; }
    public string? Currency { get; set; }
    public Customer? Customer { get; set; }
    public Product? Product { get; set; }
}
