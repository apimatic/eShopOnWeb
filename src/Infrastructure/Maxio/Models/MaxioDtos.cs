using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

internal sealed class CustomerEnvelope
{
    public CustomerDto? Customer { get; set; }
}

internal sealed class CustomerDto
{
    public int Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
    public string? Organization { get; set; }
}

internal sealed class CreateCustomerRequest
{
    public CreateCustomerPayload Customer { get; set; } = new();
}

internal sealed class CreateCustomerPayload
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string? Organization { get; set; }
}

internal sealed class ProductEnvelope
{
    public ProductDto? Product { get; set; }
}

internal sealed class ProductDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public ProductFamilyDto? ProductFamily { get; set; }
}

internal sealed class ProductFamilyDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
}

internal sealed class SubscriptionEnvelope
{
    public SubscriptionDto? Subscription { get; set; }
}

internal sealed class SubscriptionDto
{
    public int Id { get; set; }
    public string? State { get; set; }
    public long? ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public string? Reference { get; set; }
    public ProductDto? Product { get; set; }
    public CustomerDto? Customer { get; set; }
}

internal sealed class CreateSubscriptionRequest
{
    public CreateSubscriptionPayload Subscription { get; set; } = new();
}

internal sealed class CreateSubscriptionPayload
{
    public string ProductHandle { get; set; } = string.Empty;
    public int? CustomerId { get; set; }
    public string? CustomerReference { get; set; }
    public string? Reference { get; set; }
    /// <summary>
    /// Remittance (Relationship Invoicing) creates the subscription without capturing a card.
    /// The seeded plans are configured as payment-method-not-required.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";
}

internal sealed class ErrorEnvelope
{
    [JsonConverter(typeof(MaxioErrorsConverter))]
    public object? Errors { get; set; }
}
