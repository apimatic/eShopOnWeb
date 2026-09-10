using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Http;

// Wire models mirroring the Maxio Advanced Billing JSON contract. Property names map to the
// snake_case JSON via a SnakeCaseLower naming policy configured on the serializer.
// These are intentionally internal to the infrastructure layer; callers use the domain models.

internal sealed class ProductEnvelope
{
    public ProductWire? Product { get; set; }
}

internal sealed class ProductWire
{
    public long Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public bool RequireCreditCard { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
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

internal sealed class SubscriptionEnvelope
{
    public SubscriptionWire? Subscription { get; set; }
}

internal sealed class SubscriptionWire
{
    public long Id { get; set; }
    public string? State { get; set; }
    public string? PaymentCollectionMethod { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public long? ProductPriceInCents { get; set; }
    public ProductWire? Product { get; set; }
    public CustomerWire? Customer { get; set; }
}

// ----- Request payloads -----

internal sealed class CreateCustomerEnvelope
{
    public CreateCustomerWire Customer { get; set; } = new();
}

internal sealed class CreateCustomerWire
{
    public string? Email { get; set; }
    public string? Reference { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

internal sealed class CreateSubscriptionEnvelope
{
    public CreateSubscriptionWire Subscription { get; set; } = new();
}

internal sealed class CreateSubscriptionWire
{
    public string? ProductHandle { get; set; }
    public long? CustomerId { get; set; }
    public string? PaymentCollectionMethod { get; set; }
}

// ----- Error payload -----

internal sealed class MaxioErrorEnvelope
{
    public List<string>? Errors { get; set; }
}
