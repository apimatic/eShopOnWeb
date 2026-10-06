using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class ProductEnvelope
{
    [JsonPropertyName("product")]
    public ProductDto? Product { get; set; }
}

public class ProductDto
{
    [JsonPropertyName("id")]
    public int? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("price_in_cents")]
    public long? PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int? Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; set; }

    [JsonPropertyName("trial_price_in_cents")]
    public long? TrialPriceInCents { get; set; }

    [JsonPropertyName("taxable")]
    public bool? Taxable { get; set; }

    [JsonPropertyName("require_credit_card")]
    public bool? RequireCreditCard { get; set; }

    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; set; }

    [JsonPropertyName("product_family")]
    public ProductFamilyDto? ProductFamily { get; set; }

    public MaxioProduct ToModel() => new()
    {
        Id = Id ?? 0,
        Name = Name ?? string.Empty,
        Handle = Handle,
        Description = Description,
        PriceInCents = PriceInCents ?? 0,
        Interval = Interval ?? 0,
        IntervalUnit = IntervalUnit ?? string.Empty,
        TrialPriceInCents = TrialPriceInCents,
        Taxable = Taxable ?? false,
        RequireCreditCard = RequireCreditCard ?? false,
        ArchivedAt = ArchivedAt,
        ProductFamilyHandle = ProductFamily?.Handle
    };
}

public class ProductFamilyDto
{
    [JsonPropertyName("id")]
    public int? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }
}

public class CustomerEnvelope
{
    [JsonPropertyName("customer")]
    public CustomerDto? Customer { get; set; }
}

public class CustomerDto
{
    [JsonPropertyName("id")]
    public int? Id { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("organization")]
    public string? Organization { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    public static CustomerDto From(MaxioNewCustomerRequest request) => new()
    {
        FirstName = request.FirstName,
        LastName = request.LastName,
        Email = request.Email,
        Reference = request.Reference,
        Organization = request.Organization
    };

    public MaxioCustomer ToModel() => new()
    {
        Id = Id ?? 0,
        FirstName = FirstName,
        LastName = LastName,
        Email = Email,
        Reference = Reference,
        CreatedAt = CreatedAt
    };
}

public class SubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public SubscriptionDto? Subscription { get; set; }
}

public class SubscriptionDto
{
    [JsonPropertyName("id")]
    public int? Id { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("balance_in_cents")]
    public long? BalanceInCents { get; set; }

    [JsonPropertyName("product_price_in_cents")]
    public long? ProductPriceInCents { get; set; }

    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }

    [JsonPropertyName("customer_id")]
    public int? CustomerId { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("current_period_started_at")]
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("activated_at")]
    public DateTimeOffset? ActivatedAt { get; set; }

    [JsonPropertyName("canceled_at")]
    public DateTimeOffset? CanceledAt { get; set; }

    [JsonPropertyName("expires_at")]
    public DateTimeOffset? ExpiresAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("cancel_at_end_of_period")]
    public bool? CancelAtEndOfPeriod { get; set; }

    [JsonPropertyName("customer")]
    public CustomerDto? Customer { get; set; }

    [JsonPropertyName("product")]
    public ProductDto? Product { get; set; }

    public MaxioSubscription ToModel() => new()
    {
        Id = Id ?? 0,
        State = State ?? string.Empty,
        BalanceInCents = BalanceInCents ?? 0,
        ProductPriceInCents = ProductPriceInCents ?? Product?.PriceInCents ?? 0,
        ProductId = Product?.Id ?? 0,
        ProductHandle = Product?.Handle ?? ProductHandle,
        ProductName = Product?.Name,
        ProductInterval = Product?.Interval ?? 0,
        ProductIntervalUnit = Product?.IntervalUnit ?? string.Empty,
        CurrentPeriodStartedAt = CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = CurrentPeriodEndsAt,
        NextAssessmentAt = NextAssessmentAt,
        ActivatedAt = ActivatedAt,
        CanceledAt = CanceledAt,
        ExpiresAt = ExpiresAt,
        CreatedAt = CreatedAt ?? DateTimeOffset.MinValue,
        CancelAtEndOfPeriod = CancelAtEndOfPeriod,
        Reference = Reference,
        CustomerId = Customer?.Id ?? CustomerId ?? 0,
        CustomerReference = Customer?.Reference
    };
}
