using System;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

// Wire-format models for the Maxio Advanced Billing REST API (https://{subdomain}.chargify.com).
// Field names are mapped via JsonNamingPolicy.SnakeCaseLower (e.g. ProductHandle -> product_handle),
// matching the JSON shapes documented at https://developers.maxio.com and the maxio-com/ab-dotnet-sdk
// reference SDK. Only the fields this integration actually reads/writes are modeled.

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
    public CreateCustomerWire Customer { get; set; } = default!;
}

internal sealed class CreateCustomerWire
{
    public string FirstName { get; set; } = default!;
    public string LastName { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string? Reference { get; set; }
}

internal sealed class ProductEnvelope
{
    public ProductWire? Product { get; set; }
}

internal sealed class ProductWire
{
    public long Id { get; set; }
    public string Handle { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = default!;
    public DateTimeOffset? ArchivedAt { get; set; }
}

internal sealed class SubscriptionEnvelope
{
    public SubscriptionWire? Subscription { get; set; }
}

internal sealed class SubscriptionWire
{
    public long Id { get; set; }
    public string State { get; set; } = default!;
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public ProductWire? Product { get; set; }
}

internal sealed class CreateSubscriptionEnvelope
{
    public CreateSubscriptionWire Subscription { get; set; } = default!;
}

internal sealed class CreateSubscriptionWire
{
    public string ProductHandle { get; set; } = default!;
    public string CustomerReference { get; set; } = default!;
    public string PaymentCollectionMethod { get; set; } = default!;
}
