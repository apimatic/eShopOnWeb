using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

internal sealed class ProductEnvelope
{
    public ProductResource? Product { get; set; }
}

internal sealed class ProductResource
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public bool RequireCreditCard { get; set; }
    public string? ArchivedAt { get; set; }
}

internal sealed class CustomerEnvelope
{
    public CustomerResource? Customer { get; set; }
}

internal sealed class CustomerResource
{
    public int Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

internal sealed class CreateCustomerRequest
{
    public CreateCustomerPayload Customer { get; set; } = new();

    [JsonPropertyName("uniqueness_token")]
    public string? UniquenessToken { get; set; }
}

internal sealed class CreateCustomerPayload
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Organization { get; set; }
    public string Reference { get; set; } = string.Empty;
}

internal sealed class SubscriptionEnvelope
{
    public SubscriptionResource? Subscription { get; set; }
}

internal sealed class SubscriptionResource
{
    public int Id { get; set; }
    public string? State { get; set; }
    public long ProductPriceInCents { get; set; }
    public string? CurrentPeriodEndsAt { get; set; }
    public string? NextAssessmentAt { get; set; }
    public ProductResource? Product { get; set; }
}

internal sealed class CreateSubscriptionRequest
{
    public CreateSubscriptionPayload Subscription { get; set; } = new();

    [JsonPropertyName("uniqueness_token")]
    public string? UniquenessToken { get; set; }
}

internal sealed class CreateSubscriptionPayload
{
    public string ProductHandle { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string? PaymentCollectionMethod { get; set; }
}

internal sealed class SiteEnvelope
{
    public SiteResource? Site { get; set; }
}

internal sealed class SiteResource
{
    public bool? RelationshipInvoicingEnabled { get; set; }
}

internal sealed class MaxioErrorResponse
{
    public List<string>? Errors { get; set; }
}
