using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionBilling;

/// <summary>
/// A subscription plan (product) offered on the configured Maxio product family.
/// </summary>
public sealed class SubscriptionPlan
{
    public SubscriptionPlan(
        long productId,
        string handle,
        string? name,
        string? description,
        long priceInCents,
        int? interval,
        string? intervalUnit,
        int? trialInterval,
        string? trialIntervalUnit,
        bool requireCreditCard,
        bool taxable,
        DateTimeOffset? archivedAt)
    {
        ProductId = productId;
        Handle = handle;
        Name = name;
        Description = description;
        PriceInCents = priceInCents;
        Interval = interval;
        IntervalUnit = intervalUnit;
        TrialInterval = trialInterval;
        TrialIntervalUnit = trialIntervalUnit;
        RequireCreditCard = requireCreditCard;
        Taxable = taxable;
        ArchivedAt = archivedAt;
    }

    public long ProductId { get; }

    public string Handle { get; }

    public string? Name { get; }

    public string? Description { get; }

    public long PriceInCents { get; }

    public int? Interval { get; }

    public string? IntervalUnit { get; }

    public int? TrialInterval { get; }

    public string? TrialIntervalUnit { get; }

    public bool RequireCreditCard { get; }

    public bool Taxable { get; }

    public bool IsArchived => ArchivedAt.HasValue;

    public DateTimeOffset? ArchivedAt { get; }
}
