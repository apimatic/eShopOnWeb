using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

// Wire-level shapes for the Maxio Advanced Billing REST API (https://{subdomain}.chargify.com).
// Property names are mapped to Maxio's snake_case JSON via MaxioApiClient.JsonOptions, so these
// only need standard PascalCase C# names.

internal sealed class ProductEnvelope
{
    public MaxioProduct Product { get; set; } = default!;
}

internal sealed class MaxioProduct
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Handle { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public DateTimeOffset? ArchivedAt { get; set; }
}

internal sealed class CustomerEnvelope
{
    public MaxioCustomer Customer { get; set; } = default!;
}

internal sealed class MaxioCustomer
{
    public long Id { get; set; }
    public string? Reference { get; set; }
    public string Email { get; set; } = string.Empty;
}

internal sealed class CreateCustomerRequest
{
    public CreateCustomerAttributes Customer { get; set; } = default!;
}

internal sealed class CreateCustomerAttributes
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

internal sealed class SubscriptionEnvelope
{
    public MaxioSubscription Subscription { get; set; } = default!;
}

internal sealed class MaxioSubscription
{
    public long Id { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public MaxioProduct? Product { get; set; }
}

internal sealed class CreateSubscriptionRequest
{
    public CreateSubscriptionAttributes Subscription { get; set; } = default!;
    public string? UniquenessToken { get; set; }
}

internal sealed class CreateSubscriptionAttributes
{
    public string ProductHandle { get; set; } = string.Empty;
    public long CustomerId { get; set; }
    public string? PaymentCollectionMethod { get; set; }
}

internal sealed class SiteEnvelope
{
    public MaxioSite Site { get; set; } = default!;
}

internal sealed class MaxioSite
{
    public bool RelationshipInvoicingEnabled { get; set; }
}
