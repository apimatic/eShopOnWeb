using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

// Wire models for the subset of the Maxio Advanced Billing REST API this integration uses.
// Field names (snake_case) and envelopes were confirmed against the current Maxio Advanced
// Billing API (https://developers.maxio.com) and the live sandbox site.

internal sealed class SiteDto
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("subdomain")] public string Subdomain { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("currency")] public string Currency { get; set; } = string.Empty;
}

internal sealed class SiteEnvelope
{
    [JsonPropertyName("site")] public SiteDto Site { get; set; } = new SiteDto();
}

internal sealed class ProductFamilyDto
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("handle")] public string Handle { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("archived_at")] public DateTimeOffset? ArchivedAt { get; set; }
}

internal sealed class ProductFamilyEnvelope
{
    [JsonPropertyName("product_family")] public ProductFamilyDto ProductFamily { get; set; } = new ProductFamilyDto();
}

internal sealed class ProductDto
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("handle")] public string Handle { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("price_in_cents")] public long? PriceInCents { get; set; }
    [JsonPropertyName("interval")] public int? Interval { get; set; }
    [JsonPropertyName("interval_unit")] public string? IntervalUnit { get; set; }
    [JsonPropertyName("archived_at")] public DateTimeOffset? ArchivedAt { get; set; }
    [JsonPropertyName("require_credit_card")] public bool RequireCreditCard { get; set; }
    [JsonPropertyName("product_family")] public ProductFamilyDto? ProductFamily { get; set; }
}

internal sealed class ProductEnvelope
{
    [JsonPropertyName("product")] public ProductDto Product { get; set; } = new ProductDto();
}

internal sealed class CustomerDto
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("first_name")] public string? FirstName { get; set; }
    [JsonPropertyName("last_name")] public string? LastName { get; set; }
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("reference")] public string? Reference { get; set; }
    [JsonPropertyName("organization")] public string? Organization { get; set; }
}

internal sealed class CustomerEnvelope
{
    [JsonPropertyName("customer")] public CustomerDto Customer { get; set; } = new CustomerDto();
}

internal sealed class SubscriptionDto
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("state")] public string State { get; set; } = string.Empty;
    [JsonPropertyName("reference")] public string? Reference { get; set; }
    [JsonPropertyName("currency")] public string? Currency { get; set; }
    [JsonPropertyName("product_price_in_cents")] public long? ProductPriceInCents { get; set; }
    [JsonPropertyName("payment_collection_method")] public string? PaymentCollectionMethod { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
    [JsonPropertyName("activated_at")] public DateTimeOffset? ActivatedAt { get; set; }
    [JsonPropertyName("current_period_started_at")] public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    [JsonPropertyName("current_period_ends_at")] public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    [JsonPropertyName("next_assessment_at")] public DateTimeOffset? NextAssessmentAt { get; set; }
    [JsonPropertyName("product")] public ProductDto? Product { get; set; }
    [JsonPropertyName("customer")] public CustomerDto? Customer { get; set; }
}

internal sealed class SubscriptionEnvelope
{
    [JsonPropertyName("subscription")] public SubscriptionDto Subscription { get; set; } = new SubscriptionDto();
}

internal sealed class CreateCustomerRequestDto
{
    [JsonPropertyName("customer")] public CreateCustomerPayloadDto Customer { get; set; } = new CreateCustomerPayloadDto();
}

internal sealed class CreateCustomerPayloadDto
{
    [JsonPropertyName("first_name")] public string? FirstName { get; set; }
    [JsonPropertyName("last_name")] public string? LastName { get; set; }
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("reference")] public string? Reference { get; set; }
}

internal sealed class CreateSubscriptionRequestDto
{
    [JsonPropertyName("subscription")] public CreateSubscriptionPayloadDto Subscription { get; set; } = new CreateSubscriptionPayloadDto();
}

internal sealed class CreateSubscriptionPayloadDto
{
    [JsonPropertyName("product_handle")] public string? ProductHandle { get; set; }
    [JsonPropertyName("customer_id")] public int? CustomerId { get; set; }
    [JsonPropertyName("reference")] public string? Reference { get; set; }
    [JsonPropertyName("payment_collection_method")] public string? PaymentCollectionMethod { get; set; }
}

/// <summary>
/// Parsed list of human readable errors returned by Maxio, e.g. { "errors": [ "..." ] }.
/// </summary>
internal sealed class MaxioErrorEnvelope
{
    [JsonPropertyName("errors")] public List<string> Errors { get; set; } = new List<string>();
}
