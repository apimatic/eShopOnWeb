using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// Wire-format models for the Maxio Advanced Billing REST API (https://{subdomain}.chargify.com).
// Every resource is wrapped in an envelope keyed by its singular name, e.g. {"customer": {...}},
// and list endpoints return a JSON array of those envelopes. Field names are snake_case on the
// wire; the (de)serializer is configured with JsonNamingPolicy.SnakeCaseLower so the C# property
// names below map 1:1 without needing per-property attributes.

public sealed class ErrorsEnvelope
{
    public string[]? Errors { get; set; }
}

public sealed class CustomerEnvelope
{
    public CustomerWire? Customer { get; set; }
}

public sealed class CustomerWire
{
    public int Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

public sealed class CreateCustomerRequestEnvelope
{
    public CreateCustomerRequestWire Customer { get; set; } = default!;
}

public sealed class CreateCustomerRequestWire
{
    public string FirstName { get; set; } = default!;
    public string LastName { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string Reference { get; set; } = default!;
}

public sealed class ProductFamilyEnvelope
{
    public ProductFamilyWire? ProductFamily { get; set; }
}

public sealed class ProductFamilyWire
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
}

public sealed class ProductEnvelope
{
    public ProductWire? Product { get; set; }
}

public sealed class ProductWire
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public bool RequireCreditCard { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
}

public sealed class SubscriptionEnvelope
{
    public SubscriptionWire? Subscription { get; set; }
}

public sealed class SubscriptionWire
{
    public int Id { get; set; }
    public string? State { get; set; }
    public int ProductPriceInCents { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public ProductWire? Product { get; set; }
    public CustomerWire? Customer { get; set; }
}

public sealed class CreateSubscriptionRequestEnvelope
{
    public CreateSubscriptionRequestWire Subscription { get; set; } = default!;
}

public sealed class CreateSubscriptionRequestWire
{
    public string ProductHandle { get; set; } = default!;
    public int CustomerId { get; set; }

    /// <summary>
    /// "invoice" bills the balance for manual/offline collection instead of attempting an
    /// automatic card charge. Required for plans configured with no payment method captured
    /// (require_credit_card: false) that still carry a non-zero price: without this, Maxio's
    /// default "automatic" collection tries to charge a card on file and 422s when there isn't one.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "invoice";
}
