using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Subset of the Billing API customer object relevant to the eShopOnWeb integration.
/// </summary>
public class MaxioCustomer
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

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
    public DateTime? CreatedAt { get; set; }
}

public class MaxioCustomerInput
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Organization { get; set; }
    public string Reference { get; set; } = string.Empty;
}

/// <summary>
/// Subset of the Billing API product ("plan") object relevant to the eShopOnWeb integration.
/// </summary>
public class MaxioProduct
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string IntervalUnit { get; set; } = string.Empty;

    [JsonPropertyName("trial_interval")]
    public int? TrialInterval { get; set; }

    [JsonPropertyName("trial_interval_unit")]
    public string? TrialIntervalUnit { get; set; }

    [JsonPropertyName("require_credit_card")]
    public bool RequireCreditCard { get; set; }

    [JsonPropertyName("taxable")]
    public bool Taxable { get; set; }

    [JsonPropertyName("archived_at")]
    public DateTime? ArchivedAt { get; set; }

    [JsonPropertyName("product_family")]
    public MaxioProductFamilyRef? ProductFamily { get; set; }
}

public class MaxioProductFamilyRef
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }
}

/// <summary>
/// Subset of the Billing API subscription object relevant to the eShopOnWeb integration.
/// </summary>
public class MaxioSubscription
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("balance_in_cents")]
    public long BalanceInCents { get; set; }

    [JsonPropertyName("current_period_started_at")]
    public DateTime? CurrentPeriodStartedAt { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTime? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [JsonPropertyName("activated_at")]
    public DateTime? ActivatedAt { get; set; }

    [JsonPropertyName("canceled_at")]
    public DateTime? CanceledAt { get; set; }

    [JsonPropertyName("product")]
    public MaxioSubscribedProduct? Product { get; set; }

    [JsonPropertyName("customer")]
    public MaxioSubscriptionCustomerRef? Customer { get; set; }
}

public class MaxioSubscribedProduct
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string IntervalUnit { get; set; } = string.Empty;
}

public class MaxioSubscriptionCustomerRef
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }
}

public class MaxioCreateSubscriptionInput
{
    public string ProductHandle { get; set; } = string.Empty;
    public int CustomerId { get; set; }
}
