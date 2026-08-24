using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

// Wire DTOs for the Maxio Advanced Billing REST API. Field names verified
// against the live sandbox API (site responses) and the official docs at
// https://developers.maxio.com/http/advanced-billing-api.

public class MaxioCustomerDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("reference")] public string? Reference { get; set; }
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("first_name")] public string? FirstName { get; set; }
    [JsonPropertyName("last_name")] public string? LastName { get; set; }
}

public class MaxioCustomerWrapper
{
    [JsonPropertyName("customer")] public MaxioCustomerDto Customer { get; set; } = new();
}

public class MaxioProductDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("handle")] public string Handle { get; set; } = string.Empty;
    [JsonPropertyName("price_in_cents")] public long PriceInCents { get; set; }
    [JsonPropertyName("interval")] public int Interval { get; set; }
    [JsonPropertyName("interval_unit")] public string IntervalUnit { get; set; } = string.Empty;
    [JsonPropertyName("archived_at")] public DateTimeOffset? ArchivedAt { get; set; }
}

public class MaxioProductWrapper
{
    [JsonPropertyName("product")] public MaxioProductDto Product { get; set; } = new();
}

public class MaxioProductFamilyDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("handle")] public string Handle { get; set; } = string.Empty;
}

public class MaxioProductFamilyWrapper
{
    [JsonPropertyName("product_family")] public MaxioProductFamilyDto ProductFamily { get; set; } = new();
}

public class MaxioSubscriptionDto
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("state")] public string State { get; set; } = string.Empty;
    [JsonPropertyName("activated_at")] public DateTimeOffset? ActivatedAt { get; set; }
    [JsonPropertyName("current_period_ends_at")] public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    [JsonPropertyName("next_assessment_at")] public DateTimeOffset? NextAssessmentAt { get; set; }
    [JsonPropertyName("currency")] public string Currency { get; set; } = string.Empty;
    [JsonPropertyName("product_price_in_cents")] public long ProductPriceInCents { get; set; }
    [JsonPropertyName("product")] public MaxioProductDto? Product { get; set; }
    [JsonPropertyName("customer")] public MaxioCustomerDto? Customer { get; set; }
}

public class MaxioSubscriptionWrapper
{
    [JsonPropertyName("subscription")] public MaxioSubscriptionDto Subscription { get; set; } = new();
}

public class MaxioErrorResponse
{
    [JsonPropertyName("errors")] public List<string> Errors { get; set; } = new();
}

public class CreateMaxioCustomerRequest
{
    [JsonPropertyName("customer")] public CreateMaxioCustomer Customer { get; set; } = new();
}

public class CreateMaxioCustomer
{
    [JsonPropertyName("first_name")] public string FirstName { get; set; } = string.Empty;
    [JsonPropertyName("last_name")] public string LastName { get; set; } = string.Empty;
    [JsonPropertyName("email")] public string Email { get; set; } = string.Empty;
    [JsonPropertyName("reference")] public string Reference { get; set; } = string.Empty;
}

public class CreateMaxioSubscriptionRequest
{
    [JsonPropertyName("subscription")] public CreateMaxioSubscription Subscription { get; set; } = new();
}

public class CreateMaxioSubscription
{
    [JsonPropertyName("product_handle")] public string ProductHandle { get; set; } = string.Empty;
    [JsonPropertyName("customer_id")] public long CustomerId { get; set; }

    // "remittance" bills by invoice, so signup succeeds without a card on file.
    // The seeded plans require no payment method, but the default "automatic"
    // collection still demands one for the initial charge (verified: Maxio
    // returns 422 "No payment method was on file for the balance").
    [JsonPropertyName("payment_collection_method")] public string PaymentCollectionMethod { get; set; } = "remittance";
}
