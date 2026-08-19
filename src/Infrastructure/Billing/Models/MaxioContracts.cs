using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Models;

internal sealed class ProductResponse
{
    public Product? Product { get; set; }
}

internal sealed class Product
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public ProductFamily? ProductFamily { get; set; }
}

internal sealed class ProductFamily
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
}

internal sealed class CustomerResponse
{
    public Customer? Customer { get; set; }
}

internal sealed class Customer
{
    public int Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

internal sealed class CreateCustomerRequest
{
    public required CreateCustomerPayload Customer { get; set; }
}

internal sealed class CreateCustomerPayload
{
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public required string Email { get; set; }
    public required string Reference { get; set; }
}

internal sealed class SubscriptionResponse
{
    public Subscription? Subscription { get; set; }
}

internal sealed class Subscription
{
    public int Id { get; set; }
    public string? State { get; set; }
    public long ProductPriceInCents { get; set; }
    public System.DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public System.DateTimeOffset? NextAssessmentAt { get; set; }
    public string? Reference { get; set; }
    public Customer? Customer { get; set; }
    public Product? Product { get; set; }
}

internal sealed class CreateSubscriptionRequest
{
    public required CreateSubscriptionPayload Subscription { get; set; }
}

internal sealed class CreateSubscriptionPayload
{
    public required string ProductHandle { get; set; }
    public int CustomerId { get; set; }
    public string? Reference { get; set; }
    public string? PaymentCollectionMethod { get; set; }
}

internal sealed class ErrorListResponse
{
    [JsonConverter(typeof(MaxioErrorListConverter))]
    public List<string> Errors { get; set; } = new();
}
