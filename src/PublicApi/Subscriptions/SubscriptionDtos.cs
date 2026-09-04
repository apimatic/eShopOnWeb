namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlanDto
{
    public required string Handle { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public long PriceInCents { get; init; }

    public int Interval { get; init; }

    public string? IntervalUnit { get; init; }

    public bool RequiresPaymentMethod { get; init; }
}

public sealed class SubscribeRequest : BaseRequest
{
    public string? PlanHandle { get; init; }
}

public sealed class MySubscriptionDto
{
    public int Id { get; init; }

    public required string PlanHandle { get; init; }

    public required string PlanName { get; init; }

    public long PriceInCents { get; init; }

    public string? State { get; init; }

    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
}

public sealed class SubscriptionSignupResult
{
    public required MySubscriptionDto Subscription { get; init; }

    public bool Created { get; init; }
}
