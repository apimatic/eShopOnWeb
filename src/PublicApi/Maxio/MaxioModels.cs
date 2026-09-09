using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public sealed class MaxioProductFamily
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("handle")]
    public string Handle { get; set; } = string.Empty;

    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; set; }
}

public sealed class MaxioProduct
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("handle")]
    public string Handle { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("price_in_cents")]
    public int PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string IntervalUnit { get; set; } = string.Empty;

    [JsonPropertyName("trial_interval")]
    public int? TrialInterval { get; set; }

    [JsonPropertyName("trial_interval_unit")]
    public string? TrialIntervalUnit { get; set; }

    [JsonPropertyName("trial_price_in_cents")]
    public int? TrialPriceInCents { get; set; }

    [JsonPropertyName("require_credit_card")]
    public bool RequireCreditCard { get; set; }

    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; set; }

    [JsonPropertyName("product_family")]
    public MaxioProductFamily? ProductFamily { get; set; }
}

public sealed class MaxioCustomer
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("organization")]
    public string? Organization { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }
}

public sealed class MaxioSubscription
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("balance_in_cents")]
    public int BalanceInCents { get; set; }

    [JsonPropertyName("current_period_starts_at")]
    public DateTimeOffset? CurrentPeriodStartsAt { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("activated_at")]
    public DateTimeOffset? ActivatedAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("canceled_at")]
    public DateTimeOffset? CanceledAt { get; set; }

    [JsonPropertyName("cancel_at_end_of_period")]
    public bool CancelAtEndOfPeriod { get; set; }

    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; set; }

    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }

    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}

/// <summary>
/// Inline customer attributes for a signup (subscription creation) request.
/// </summary>
public sealed class MaxioCustomerAttributes
{
    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Maxio external reference; we set it to the eShopOnWeb user id so the
    /// Maxio customer can be found again deterministically.
    /// </summary>
    [JsonPropertyName("reference")]
    public string Reference { get; set; } = string.Empty;
}

public sealed class MaxioCreateSubscriptionRequest
{
    /// <summary>
    /// Duplicate-prevention token (Maxio "uniqueness_token"); replays within an
    /// hour return 409 Conflict instead of creating a second subscription.
    /// </summary>
    [JsonPropertyName("uniqueness_token")]
    public string UniquenessToken { get; set; } = string.Empty;

    [JsonPropertyName("subscription")]
    public MaxioCreateSubscriptionBody Subscription { get; set; } = new();
}

public sealed class MaxioCreateSubscriptionBody
{
    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }

    /// <summary>
    /// "automatic" (card on file, default) or "remittance" (no payment profile
    /// required at signup; the seeded plans do not require a payment method).
    /// </summary>
    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>
    /// Set when the Maxio customer already exists; mutually exclusive with
    /// <see cref="CustomerAttributes"/>.
    /// </summary>
    [JsonPropertyName("customer_id")]
    public long? CustomerId { get; set; }

    [JsonPropertyName("customer_attributes")]
    public MaxioCustomerAttributes? CustomerAttributes { get; set; }
}

public sealed class MaxioApiError
{
    [JsonPropertyName("errors")]
    public List<string> Errors { get; set; } = new();
}
