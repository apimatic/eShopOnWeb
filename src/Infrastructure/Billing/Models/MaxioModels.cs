using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Models;

public sealed class CustomerResponse
{
    public Customer? Customer { get; set; }
}

public sealed class Customer
{
    public int? Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

public sealed class CreateCustomerRequest
{
    public CreateCustomer Customer { get; set; } = new();
}

public sealed class CreateCustomer
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

public sealed class ProductResponse
{
    public Product? Product { get; set; }
}

public sealed class Product
{
    public int? Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long? PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public ProductFamily? ProductFamily { get; set; }
}

public sealed class ProductFamily
{
    public int? Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
}

public sealed class SubscriptionResponse
{
    public Subscription? Subscription { get; set; }
}

public sealed class Subscription
{
    public int? Id { get; set; }
    public string? State { get; set; }
    public long? ProductPriceInCents { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public string? Reference { get; set; }
    public Product? Product { get; set; }
    public Customer? Customer { get; set; }
}

public sealed class CreateSubscriptionRequest
{
    public CreateSubscription Subscription { get; set; } = new();
}

public sealed class CreateSubscription
{
    public string? ProductHandle { get; set; }
    public int? CustomerId { get; set; }
    public string? CustomerReference { get; set; }
    public string? Reference { get; set; }
    public string? PaymentCollectionMethod { get; set; }
}

public sealed class ErrorListResponse
{
    [JsonConverter(typeof(MaxioErrorListConverter))]
    public List<string> Errors { get; set; } = new();
}
