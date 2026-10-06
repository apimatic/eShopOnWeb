using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

// Response models mirror the Maxio Advanced Billing OpenAPI specification
// (maxio-spec/openapi.yaml). Property names are mapped to the spec's snake_case
// JSON via JsonNamingPolicy.SnakeCaseLower.

public class MaxioProductFamily
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public string? AccountingCode { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
}

public class MaxioProduct
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public bool Taxable { get; set; }
    public bool RequireCreditCard { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}

public class MaxioCustomer
{
    public int Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public class MaxioSubscription
{
    public int Id { get; set; }
    public string? State { get; set; }
    public int BalanceInCents { get; set; }
    public int ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string? Reference { get; set; }
    public string? PaymentCollectionMethod { get; set; }
    public MaxioProduct? Product { get; set; }
    public MaxioCustomer? Customer { get; set; }
}

// Envelope types wrap the top-level object keys used by the Maxio API.

public class MaxioProductFamilyEnvelope
{
    public MaxioProductFamily? ProductFamily { get; set; }
}

public class MaxioProductEnvelope
{
    public MaxioProduct? Product { get; set; }
}

public class MaxioCustomerEnvelope
{
    public MaxioCustomer? Customer { get; set; }
}

public class MaxioSubscriptionEnvelope
{
    public MaxioSubscription? Subscription { get; set; }
}

// Request models for the create endpoints.

public class MaxioCreateCustomerRequest
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

public class MaxioCreateCustomerEnvelope
{
    public MaxioCreateCustomerRequest? Customer { get; set; }
}

public class MaxioCreateSubscriptionRequest
{
    public string? ProductHandle { get; set; }
    public int? CustomerId { get; set; }
    public string? Reference { get; set; }
    public string? PaymentCollectionMethod { get; set; }
}

public class MaxioCreateSubscriptionEnvelope
{
    public MaxioCreateSubscriptionRequest? Subscription { get; set; }
}
