using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Wire-format (snake_case) DTOs for the Maxio Advanced Billing REST API.
/// These types mirror the billing system's JSON contract and stay internal to
/// the infrastructure layer.
/// </summary>
internal sealed class WireCustomerWrapper
{
    [JsonPropertyName("customer")]
    public WireCustomer? Customer { get; set; }
}

internal sealed class WireCustomer
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }
}

internal sealed class WireSubscriptionWrapper
{
    [JsonPropertyName("subscription")]
    public WireSubscription? Subscription { get; set; }
}

internal sealed class WireSubscription
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("product_price_in_cents")]
    public int? ProductPriceInCents { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("activated_at")]
    public DateTimeOffset? ActivatedAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("product")]
    public WireProduct? Product { get; set; }
}

internal sealed class WireProductWrapper
{
    [JsonPropertyName("product")]
    public WireProduct? Product { get; set; }
}

internal sealed class WireProduct
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("price_in_cents")]
    public int PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int? Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; set; }

    [JsonPropertyName("require_credit_card")]
    public bool RequireCreditCard { get; set; }

    [JsonPropertyName("taxable")]
    public bool Taxable { get; set; }

    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; set; }
}

internal sealed class WireCreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public WireCreateCustomer Customer { get; set; } = new();
}

internal sealed class WireCreateCustomer
{
    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("reference")]
    public string Reference { get; set; } = string.Empty;

    [JsonPropertyName("organization")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Organization { get; set; }
}

internal sealed class WireCreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public WireCreateSubscription Subscription { get; set; } = new();

    [JsonPropertyName("uniqueness_token")]
    public string UniquenessToken { get; set; } = string.Empty;
}

internal sealed class WireCreateSubscription
{
    [JsonPropertyName("customer_id")]
    public long CustomerId { get; set; }

    [JsonPropertyName("product_id")]
    public long ProductId { get; set; }

    [JsonPropertyName("reference")]
    public string Reference { get; set; } = string.Empty;

    [JsonPropertyName("payment_collection_method")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PaymentCollectionMethod { get; set; }
}

internal static class WireMapping
{
    public static ApplicationCore.Maxio.SubscriptionPlan ToPlan(this WireProduct product) =>
        new()
        {
            Id = product.Id,
            Handle = product.Handle ?? string.Empty,
            Name = product.Name ?? string.Empty,
            Description = product.Description,
            PriceInCents = product.PriceInCents,
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit,
            RequiresPaymentMethod = product.RequireCreditCard,
            Taxable = product.Taxable,
        };

    public static ApplicationCore.Maxio.MaxioCustomer ToCustomer(this WireCustomer customer) =>
        new()
        {
            Id = customer.Id,
            Reference = customer.Reference,
            FirstName = customer.FirstName ?? string.Empty,
            LastName = customer.LastName ?? string.Empty,
            Email = customer.Email ?? string.Empty,
        };

    public static ApplicationCore.Maxio.MaxioSubscription ToSubscription(this WireSubscription subscription) =>
        new()
        {
            Id = subscription.Id,
            State = subscription.State ?? string.Empty,
            Reference = subscription.Reference,
            ProductId = subscription.Product?.Id,
            ProductHandle = subscription.Product?.Handle,
            ProductName = subscription.Product?.Name,
            PriceInCents = subscription.ProductPriceInCents,
            Currency = subscription.Currency,
            NextBillingAt = subscription.CurrentPeriodEndsAt,
            ActivatedAt = subscription.ActivatedAt,
            CreatedAt = subscription.CreatedAt ?? default,
        };
}
