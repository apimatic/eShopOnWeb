using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Dtos;

internal sealed class MaxioSubscriptionEnvelope
{
    public MaxioSubscription? Subscription { get; set; }
}

internal sealed class MaxioSubscription
{
    public long Id { get; set; }

    public string? State { get; set; }

    public long BalanceInCents { get; set; }

    public long ProductPriceInCents { get; set; }

    public string? Currency { get; set; }

    public string? PaymentCollectionMethod { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the next renewal charge is assessed; diverges from the period end after a failed payment.</summary>
    public DateTimeOffset? NextAssessmentAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public MaxioProduct? Product { get; set; }

    public MaxioCustomer? Customer { get; set; }
}

internal sealed class MaxioCreateSubscriptionRequest
{
    public required MaxioCreateSubscriptionAttributes Subscription { get; init; }

    /// <summary>
    /// Duplicate-prevention token. A second request carrying the same value inside the provider's
    /// window is rejected with 409 instead of creating a second subscription.
    /// </summary>
    public required string UniquenessToken { get; init; }
}

internal sealed class MaxioCreateSubscriptionAttributes
{
    public required string ProductHandle { get; init; }

    public required long CustomerId { get; init; }

    public required string PaymentCollectionMethod { get; init; }
}
