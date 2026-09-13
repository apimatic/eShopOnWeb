using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioProductFamily
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("handle")]
    public string Handle { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}

public class MaxioProductFamilyResponse
{
    [JsonPropertyName("product_family")]
    public MaxioProductFamily ProductFamily { get; set; } = new();
}

public class MaxioProduct
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("handle")]
    public string Handle { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("price_in_cents")]
    public int PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string IntervalUnit { get; set; } = string.Empty;

    [JsonPropertyName("require_credit_card")]
    public bool RequireCreditCard { get; set; }

    [JsonPropertyName("trial_price_in_cents")]
    public int? TrialPriceInCents { get; set; }

    [JsonPropertyName("taxable")]
    public bool Taxable { get; set; }

    [JsonPropertyName("product_family")]
    public MaxioProductFamily? ProductFamily { get; set; }
}

public class MaxioProductResponse
{
    [JsonPropertyName("product")]
    public MaxioProduct Product { get; set; } = new();
}

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
    public string Reference { get; set; } = string.Empty;

    [JsonPropertyName("organization")]
    public string? Organization { get; set; }
}

public class MaxioCustomerResponse
{
    [JsonPropertyName("customer")]
    public MaxioCustomer Customer { get; set; } = new();
}

public class MaxioCreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public MaxioCustomerBody Customer { get; set; } = new();
}

public class MaxioCustomerBody
{
    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("reference")]
    public string Reference { get; set; } = string.Empty;
}

public class MaxioSubscription
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("balance_in_cents")]
    public int BalanceInCents { get; set; }

    [JsonPropertyName("product_price_in_cents")]
    public int ProductPriceInCents { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public string? NextAssessmentAt { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public string? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("activated_at")]
    public string? ActivatedAt { get; set; }

    [JsonPropertyName("created_at")]
    public string CreatedAt { get; set; } = string.Empty;

    [JsonPropertyName("canceled_at")]
    public string? CanceledAt { get; set; }

    [JsonPropertyName("cancel_at_end_of_period")]
    public bool? CancelAtEndOfPeriod { get; set; }

    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }

    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

public class MaxioSubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription Subscription { get; set; } = new();
}

public class MaxioCreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public MaxioSubscriptionBody Subscription { get; set; } = new();
}

public class MaxioSubscriptionBody
{
    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; set; } = string.Empty;

    [JsonPropertyName("customer_reference")]
    public string CustomerReference { get; set; } = string.Empty;

    [JsonPropertyName("payment_collection_method")]
    public string PaymentCollectionMethod { get; set; } = "remittance";
}

public class MaxioErrorResponse
{
    [JsonPropertyName("errors")]
    public string[] Errors { get; set; } = Array.Empty<string>();
}
