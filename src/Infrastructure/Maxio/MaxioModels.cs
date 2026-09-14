using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// Wire models for the subset of the Maxio Advanced Billing API surface this integration
// uses (maxio-spec/openapi.yaml: Product, Product-Family, Customer, Subscription, and
// their "*-Response" envelope wrappers). Only the fields eShopOnWeb actually consumes are
// mapped; System.Text.Json ignores the rest.

public sealed class MaxioProductFamily
{
    public long Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class MaxioProduct
{
    public long Id { get; set; }
    public string? Handle { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public bool RequireCreditCard { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public MaxioProductFamily ProductFamily { get; set; } = new();
}

public sealed class MaxioProductEnvelope
{
    public MaxioProduct Product { get; set; } = new();
}

public sealed class MaxioCustomer
{
    public long Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

public sealed class MaxioCustomerEnvelope
{
    public MaxioCustomer Customer { get; set; } = new();
}

public sealed class MaxioSubscription
{
    public long Id { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? Reference { get; set; }
    public long ProductPriceInCents { get; set; }
    public MaxioProduct Product { get; set; } = new();
    public MaxioCustomer Customer { get; set; } = new();
}

public sealed class MaxioSubscriptionEnvelope
{
    public MaxioSubscription Subscription { get; set; } = new();
}

// Request payloads. Nested types mirror Maxio's "{ root_key: { ... } }" convention
// (see Create-Customer-Request.yaml / Create-Subscription-Request.yaml).

public sealed class CreateCustomerRequest
{
    public CreateCustomerAttributes Customer { get; set; } = new();
}

public sealed class CreateCustomerAttributes
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

public sealed class CreateSubscriptionRequest
{
    public CreateSubscriptionAttributes Subscription { get; set; } = new();
}

public sealed class CreateSubscriptionAttributes
{
    public string ProductHandle { get; set; } = string.Empty;
    public string CustomerReference { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;

    /// <summary>
    /// One of Collection-Method.yaml's non-automatic values ("remittance" on Relationship
    /// Invoicing sites, "invoice" on legacy Statements Architecture sites). Required so
    /// signup doesn't attempt an immediate card charge for plans configured with no payment
    /// method on file.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = string.Empty;
}
