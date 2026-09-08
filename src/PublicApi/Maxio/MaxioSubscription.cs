using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// A Maxio Subscription (components/schemas/Subscription.yaml). The subscription is the
/// source of truth for plan/price/state/next-billing-date confirmation.
/// </summary>
public sealed class MaxioSubscription
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public long BalanceInCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public string? Reference { get; set; }
    public long ProductPriceInCents { get; set; }
    public string? PaymentCollectionMethod { get; set; }
    public MaxioProduct? Product { get; set; }
    public MaxioCustomer? Customer { get; set; }
}

/// <summary>Single subscription read/create response wrapper: <c>{ "subscription": { ... } }</c>.</summary>
public sealed class MaxioSubscriptionResponse
{
    public MaxioSubscription Subscription { get; set; } = new();
}

/// <summary>POST /subscriptions.json request body: <c>{ "subscription": { ... } }</c>.</summary>
public sealed class CreateMaxioSubscriptionRequest
{
    public CreateMaxioSubscriptionInput Subscription { get; set; } = new();
}

/// <summary>
/// Subscription create payload. The <see cref="Reference"/> acts as an application
/// supplied idempotency key: Maxio enforces uniqueness site-wide and rejects a second
/// subscription carrying the same reference with a 422.
/// </summary>
public sealed class CreateMaxioSubscriptionInput
{
    /// <summary>Identify the product by its API handle.</summary>
    public string? ProductHandle { get; set; }

    /// <summary>Identify an existing Maxio customer by its reference.</summary>
    public string? CustomerReference { get; set; }

    /// <summary>
    /// "remittance" is used so subscriptions can be created without a payment profile
    /// (the seeded plans require no payment method).
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>Stable, site-unique reference used for idempotent creation.</summary>
    public string? Reference { get; set; }
}
