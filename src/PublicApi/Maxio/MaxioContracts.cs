using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

// Data-transfer types that mirror the Maxio Billing API JSON payloads. The Maxio API uses
// snake_case JSON; every request/response is (de)serialized with a snake_case naming policy,
// so the CLR property names below intentionally stay PascalCase.
//
// Only the members this integration needs are modeled; unknown JSON members are ignored.

/// <summary>Attributes used when creating a customer (POST /customers.json).</summary>
public sealed class CustomerAttributes
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
    public string? Organization { get; set; }
    public string? Phone { get; set; }
}

/// <summary>A customer as returned by the Billing API.</summary>
public sealed class MaxioCustomer
{
    public long Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
    public string? Organization { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>Wrapper for single-customer responses.</summary>
public sealed class MaxioCustomerEnvelope
{
    public MaxioCustomer? Customer { get; set; }
}

/// <summary>A product family as returned by the Billing API.</summary>
public sealed class MaxioProductFamily
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
}

/// <summary>A product as returned by the Billing API.</summary>
public sealed class MaxioProduct
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long? PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public bool RequireCreditCard { get; set; }
    public bool Taxable { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}

/// <summary>Wrapper for single-product responses.</summary>
public sealed class MaxioProductEnvelope
{
    public MaxioProduct? Product { get; set; }
}

/// <summary>Attributes used when creating a subscription (POST /subscriptions.json).</summary>
public sealed class SubscriptionAttributes
{
    public string? CustomerReference { get; set; }
    public string? ProductHandle { get; set; }

    /// <summary>
    /// "remittance" bills the subscription by invoice without attempting to collect a payment,
    /// which is how a plan that does not require a payment method is enrolled.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }
}

/// <summary>Body of the create-subscription request.</summary>
public sealed class CreateSubscriptionEnvelope
{
    public SubscriptionAttributes? Subscription { get; set; }

    /// <summary>
    /// Supplied to the Billing API duplicate-prevention guard so that retrying a request that
    /// timed out cannot create two subscriptions.
    /// </summary>
    public string? UniquenessToken { get; set; }
}

/// <summary>Wrapper for single-subscription responses.</summary>
public sealed class MaxioSubscriptionEnvelope
{
    public MaxioSubscription? Subscription { get; set; }
}

/// <summary>A subscription as returned by the Billing API.</summary>
public sealed class MaxioSubscription
{
    public long Id { get; set; }
    public string? State { get; set; }
    public long? BalanceInCents { get; set; }
    public long? ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    /// <summary>End of the current billing period; when the next renewal will be assessed.</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? PaymentCollectionMethod { get; set; }
    public string? Reference { get; set; }
    public MaxioProduct? Product { get; set; }
    public MaxioCustomer? Customer { get; set; }
}
