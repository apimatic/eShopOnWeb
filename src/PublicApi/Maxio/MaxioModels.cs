using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

// Wire models for the Maxio Advanced Billing API, hand-written against maxio-spec/openapi.yaml.
// (De)serialization uses JsonNamingPolicy.SnakeCaseLower, so these PascalCase properties map to the
// spec's snake_case fields. Only the fields this integration consumes are modeled.

// --- Product Families (Product-Family-Response.yaml / Product-Family.yaml) ---

public class MaxioProductFamilyResponse
{
    public MaxioProductFamily? ProductFamily { get; set; }
}

public class MaxioProductFamily
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
}

// --- Products (Product-Response.yaml / Product.yaml) ---

public class MaxioProductResponse
{
    public MaxioProduct? Product { get; set; }
}

public class MaxioProduct
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public bool RequireCreditCard { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
}

// --- Customers (Customer-Response.yaml / Customer.yaml, Create-Customer-Request.yaml) ---

public class MaxioCustomerResponse
{
    public MaxioCustomer? Customer { get; set; }
}

public class MaxioCustomer
{
    public long Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
    public string? Organization { get; set; }
}

public class CreateMaxioCustomerRequest
{
    public CreateMaxioCustomer Customer { get; set; } = new();
}

public class CreateMaxioCustomer
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
}

// --- Subscriptions (Subscription-Response.yaml / Subscription.yaml, Create-Subscription-Request.yaml) ---

public class MaxioSubscriptionResponse
{
    public MaxioSubscription? Subscription { get; set; }
}

public class MaxioSubscription
{
    public long Id { get; set; }
    public string? State { get; set; }
    public long? ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string? Reference { get; set; }
    public MaxioCustomer? Customer { get; set; }
    public MaxioProduct? Product { get; set; }
}

public class CreateMaxioSubscriptionRequest
{
    public CreateMaxioSubscription Subscription { get; set; } = new();
}

public class CreateMaxioSubscription
{
    public string? ProductHandle { get; set; }
    public long? CustomerId { get; set; }
    public string? Reference { get; set; }

    /// <summary>Per the spec's Collection-Method enum. "remittance" bills by invoice, so signup
    /// succeeds without a payment method on file (the seeded plans do not require one).</summary>
    public string? PaymentCollectionMethod { get; set; }
}
