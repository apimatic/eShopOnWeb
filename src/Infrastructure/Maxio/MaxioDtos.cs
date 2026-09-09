using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// Wire models for the Maxio Billing API. All JSON property names are snake_case,
// handled by the serializer naming policy in MaxioApiClient. Only the subset of
// attributes the integration consumes is modeled.

public sealed class MaxioProductFamilyDto
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
}

public sealed class MaxioProductDto
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public int? TrialInterval { get; set; }
    public string? TrialIntervalUnit { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public bool? RequireCreditCard { get; set; }
    public bool? Taxable { get; set; }
    public MaxioProductFamilyDto? ProductFamily { get; set; }
}

public sealed class MaxioCustomerDto
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Organization { get; set; }
}

public sealed class MaxioSubscriptionDto
{
    public int Id { get; set; }
    public string? State { get; set; }
    public long? ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public MaxioProductDto? Product { get; set; }
    public MaxioCustomerDto? Customer { get; set; }
}

// Request/response envelopes. The Billing API wraps single resources
// ({"product": {...}}) and returns arrays of wrapped resources.

public sealed class MaxioProductResponse
{
    public MaxioProductDto? Product { get; set; }
}

public sealed class MaxioCustomerResponse
{
    public MaxioCustomerDto? Customer { get; set; }
}

public sealed class MaxioSubscriptionResponse
{
    public MaxioSubscriptionDto? Subscription { get; set; }
}

public sealed class MaxioNewCustomer
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
    public string? Organization { get; set; }
}

public sealed class MaxioCreateCustomerRequest
{
    public MaxioNewCustomer Customer { get; set; } = new();
}

public sealed class MaxioNewSubscription
{
    public string? ProductHandle { get; set; }
    public int? CustomerId { get; set; }
    public string? PaymentCollectionMethod { get; set; }
}

public sealed class MaxioCreateSubscriptionRequest
{
    public MaxioNewSubscription Subscription { get; set; } = new();
    public string? UniquenessToken { get; set; }
}

public sealed class MaxioErrorResponse
{
    public string[]? Errors { get; set; }
}
