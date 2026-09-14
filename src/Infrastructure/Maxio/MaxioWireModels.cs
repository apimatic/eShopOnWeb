using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// These types mirror the JSON shapes documented for the Maxio Advanced Billing (Chargify) API:
// single-resource endpoints return an object wrapped under the resource's name (e.g. {"customer": {...}}),
// list endpoints return a bare JSON array of such wrapped objects.

internal sealed class CustomerEnvelope
{
    public CustomerWire? Customer { get; set; }
}

internal sealed class CustomerWire
{
    public long Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

internal sealed class CreateCustomerEnvelope
{
    public CreateCustomerWire Customer { get; set; } = null!;
}

internal sealed class CreateCustomerWire
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

internal sealed class ProductEnvelope
{
    public ProductWire? Product { get; set; }
}

internal sealed class ProductWire
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public DateTimeOffset? ArchivedAt { get; set; }
}

internal sealed class SubscriptionEnvelope
{
    public SubscriptionWire? Subscription { get; set; }
}

internal sealed class CreateSubscriptionEnvelope
{
    public CreateSubscriptionWire Subscription { get; set; } = null!;
}

internal sealed class CreateSubscriptionWire
{
    public string ProductHandle { get; set; } = string.Empty;
    public long CustomerId { get; set; }
    public string Reference { get; set; } = string.Empty;

    // The seeded plans don't require a payment method (require_credit_card: false), so with no
    // payment profile on file the default "automatic" collection would fail immediately at signup
    // trying to charge a nonexistent card. "invoice" (remittance billing) defers collection to a
    // manually-settled invoice instead, matching the plans' no-card-required configuration.
    public string PaymentCollectionMethod { get; set; } = "invoice";
}

internal sealed class SubscriptionWire
{
    public long Id { get; set; }
    public string State { get; set; } = string.Empty;
    public long ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public ProductWire? Product { get; set; }
}
