using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

#region Customer DTOs

public class CreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public CreateCustomerRequestDto? Customer { get; set; }
}

public class CreateCustomerRequestDto
{
    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}

public class CustomerCreateResponse
{
    [JsonPropertyName("customer")]
    public CustomerResponseDto? Customer { get; set; }
}

public class CustomerResponseDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

public class CustomerLookupResponse
{
    [JsonPropertyName("customer")]
    public CustomerResponseDto? Customer { get; set; }
}

#endregion

#region Subscription DTOs

public class MaxioCreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public CreateSubscriptionRequestDto? Subscription { get; set; }
}

public class CreateSubscriptionRequestDto
{
    [JsonPropertyName("customer_id")]
    public int CustomerId { get; set; }

    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }

    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; set; }
}

public class SubscriptionCreateResponse
{
    [JsonPropertyName("subscription")]
    public SubscriptionDto? Subscription { get; set; }
}

public class SubscriptionsListResponse
{
    [JsonPropertyName("subscriptions")]
    public List<SubscriptionDto>? Subscriptions { get; set; }
}

#endregion

#region Product DTOs

public class ProductDetailsDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("price_in_cents")]
    public int PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; set; }

    [JsonPropertyName("accounting_code")]
    public string? AccountingCode { get; set; }

    [JsonPropertyName("require_credit_card")]
    public bool? RequireCreditCard { get; set; }

    [JsonPropertyName("require_billing_address")]
    public bool? RequireBillingAddress { get; set; }

    [JsonPropertyName("trial_price_in_cents")]
    public int? TrialPriceInCents { get; set; }

    [JsonPropertyName("trial_interval")]
    public int? TrialInterval { get; set; }
}

#endregion
