using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

/// <summary>OpenAPI <c>Customer-Response</c>.</summary>
internal sealed class CustomerResponse
{
    public Customer? Customer { get; set; }
}

/// <summary>OpenAPI <c>Customer</c> / <c>Create-Customer</c> (fields we consume).</summary>
internal sealed class Customer
{
    public int? Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

/// <summary>OpenAPI <c>Create-Customer-Request</c>.</summary>
internal sealed class CreateCustomerRequest
{
    public CreateCustomer Customer { get; set; } = new();
}

/// <summary>OpenAPI <c>Create-Customer</c>.</summary>
internal sealed class CreateCustomer
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
}

/// <summary>OpenAPI <c>Product-Response</c>.</summary>
internal sealed class ProductResponse
{
    public Product? Product { get; set; }
}

/// <summary>OpenAPI <c>Product</c> (fields we consume).</summary>
internal sealed class Product
{
    public int? Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public int? PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public ProductFamily? ProductFamily { get; set; }
}

/// <summary>OpenAPI <c>Product-Family</c>.</summary>
internal sealed class ProductFamily
{
    public int? Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
}

/// <summary>OpenAPI <c>Subscription-Response</c>.</summary>
internal sealed class SubscriptionResponse
{
    public Subscription? Subscription { get; set; }
}

/// <summary>OpenAPI <c>Subscription</c> (fields we consume).</summary>
internal sealed class Subscription
{
    public int? Id { get; set; }
    public string? State { get; set; }
    public int? ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public string? Reference { get; set; }
    public Product? Product { get; set; }
    public Customer? Customer { get; set; }
}

/// <summary>OpenAPI <c>Create-Subscription-Request</c>.</summary>
internal sealed class CreateSubscriptionRequest
{
    public CreateSubscription Subscription { get; set; } = new();
}

/// <summary>OpenAPI <c>Create-Subscription</c> (fields we send).</summary>
internal sealed class CreateSubscription
{
    public string? ProductHandle { get; set; }
    public int? CustomerId { get; set; }
    public string? CustomerReference { get; set; }
    public string? Reference { get; set; }
    public string? PaymentCollectionMethod { get; set; }
}

/// <summary>OpenAPI <c>Error-List-Response</c> / <c>Customer-Error-Response</c> (flexible errors member).</summary>
internal sealed class ErrorResponse
{
    [JsonConverter(typeof(MaxioErrorsConverter))]
    public string[]? Errors { get; set; }
}
