using System;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

// Wire-format models mirroring the JSON shapes defined in maxio-spec/openapi.yaml
// (components/schemas/Product.yaml, Customer.yaml, Subscription.yaml and their
// Create-*-Request counterparts). These are intentionally separate from the
// ApplicationCore.Maxio domain models: this file is the only place that needs to know
// about Maxio's envelope-per-resource wire shape and snake_case field names.

internal sealed class ProductEnvelope
{
    public ProductWire? Product { get; set; }
}

internal sealed class ProductWire
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
}

internal sealed class CustomerEnvelope
{
    public CustomerWire? Customer { get; set; }
}

internal sealed class CustomerWire
{
    public long Id { get; set; }
    public string? Reference { get; set; }
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

internal sealed class CreateCustomerEnvelope
{
    public CreateCustomerWire Customer { get; set; } = new();
}

internal sealed class CreateCustomerWire
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

internal sealed class SubscriptionEnvelope
{
    public SubscriptionWire? Subscription { get; set; }
}

internal sealed class SubscriptionWire
{
    public long Id { get; set; }
    public string? Reference { get; set; }
    public string? State { get; set; }
    public long ProductPriceInCents { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public CustomerWire? Customer { get; set; }
    public ProductWire? Product { get; set; }
}

internal sealed class CreateSubscriptionEnvelope
{
    public CreateSubscriptionWire Subscription { get; set; } = new();
}

internal sealed class CreateSubscriptionWire
{
    public string ProductHandle { get; set; } = string.Empty;
    public long CustomerId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string PaymentCollectionMethod { get; set; } = string.Empty;
}
