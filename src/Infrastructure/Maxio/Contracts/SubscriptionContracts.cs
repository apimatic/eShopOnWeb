using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

internal sealed class SubscriptionEnvelope
{
    public SubscriptionResource? Subscription { get; set; }
}

internal sealed class SubscriptionResource
{
    public int Id { get; set; }
    public string? State { get; set; }
    public long ProductPriceInCents { get; set; }
    public long BalanceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When Maxio will next attempt to capture payment; the shopper's next billing date.</summary>
    public DateTimeOffset? NextAssessmentAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public DateTimeOffset? TrialEndedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public ProductResource? Product { get; set; }
    public CustomerResource? Customer { get; set; }
}

/// <summary>
/// Request body for POST /subscriptions.json. The uniqueness token sits beside the subscription
/// object rather than inside it, which is how Maxio's duplicate prevention expects it.
/// </summary>
internal sealed class CreateSubscriptionRequest
{
    public CreateSubscriptionAttributes Subscription { get; set; } = new();
    public string? UniquenessToken { get; set; }
}

internal sealed class CreateSubscriptionAttributes
{
    public int CustomerId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;

    /// <summary>How Maxio should collect payment, e.g. "automatic" or "remittance".</summary>
    public string? PaymentCollectionMethod { get; set; }
}
