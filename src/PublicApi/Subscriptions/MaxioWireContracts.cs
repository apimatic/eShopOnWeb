using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

// These types intentionally mirror the request and response shapes in maxio-spec/openapi.yaml.
internal sealed class MaxioProductFamilyResponse { public MaxioProductFamily ProductFamily { get; init; } = new(); }
internal sealed class MaxioProductFamily { public int Id { get; init; } public string Handle { get; init; } = string.Empty; }
internal sealed class MaxioProductResponse { public MaxioProduct Product { get; init; } = new(); }
internal sealed class MaxioProduct
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Handle { get; init; }
    public string? Description { get; init; }
    public long PriceInCents { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
    public DateTimeOffset? ArchivedAt { get; init; }
}
internal sealed class MaxioCustomerResponse { public MaxioCustomer Customer { get; init; } = new(); }
internal sealed class MaxioCustomer { public int Id { get; init; } public string? Reference { get; init; } }
internal sealed class MaxioSubscriptionResponse { public MaxioSubscription Subscription { get; init; } = new(); }
internal sealed class MaxioSubscription
{
    public int Id { get; init; }
    public string State { get; init; } = string.Empty;
    public long ProductPriceInCents { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public DateTimeOffset? NextAssessmentAt { get; init; }
    public string? Reference { get; init; }
    public MaxioProduct Product { get; init; } = new();
}

internal sealed class CreateCustomerRequest { public CreateCustomer Customer { get; init; } = new(); }
internal sealed class CreateCustomer
{
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Reference { get; init; } = string.Empty;
}
internal sealed class CreateSubscriptionRequest { public CreateSubscription Subscription { get; init; } = new(); }
internal sealed class CreateSubscription
{
    public string ProductHandle { get; init; } = string.Empty;
    public int CustomerId { get; init; }
    public string Reference { get; init; } = string.Empty;
    public string PaymentCollectionMethod { get; init; } = "invoice";
}
