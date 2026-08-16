using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// Internal wire models for the Maxio Advanced Billing REST API. Property names are converted to/from
// snake_case by the shared JsonSerializerOptions (JsonNamingPolicy.SnakeCaseLower), so they mirror the
// documented JSON fields (e.g. PriceInCents <-> price_in_cents).

internal sealed class MaxioCustomerEnvelope
{
    public MaxioCustomer? Customer { get; set; }
}

internal sealed class MaxioCustomer
{
    public long Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}

internal sealed class MaxioProductFamilyEnvelope
{
    public MaxioProductFamily? ProductFamily { get; set; }
}

internal sealed class MaxioProductFamily
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
}

internal sealed class MaxioProductEnvelope
{
    public MaxioProduct? Product { get; set; }
}

internal sealed class MaxioProduct
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}

internal sealed class MaxioSubscriptionEnvelope
{
    public MaxioSubscription? Subscription { get; set; }
}

internal sealed class MaxioSubscription
{
    public long Id { get; set; }
    public string? State { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public long? CurrentBillingAmountInCents { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public MaxioProduct? Product { get; set; }
    public MaxioCustomer? Customer { get; set; }
}

// Request payloads.

internal sealed class CreateCustomerEnvelope
{
    public CustomerAttributes Customer { get; set; } = new();
}

internal sealed class CustomerAttributes
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

internal sealed class CreateSubscriptionEnvelope
{
    public SubscriptionAttributes Subscription { get; set; } = new();
}

internal sealed class SubscriptionAttributes
{
    public string? ProductHandle { get; set; }
    public long CustomerId { get; set; }
    public string? PaymentCollectionMethod { get; set; }
}

// Error payloads. Maxio returns either {"errors": ["msg", ...]} or {"errors": {"field": ["msg"]}}.

internal sealed class MaxioErrorArrayResponse
{
    public List<string>? Errors { get; set; }
}

internal sealed class MaxioErrorMapResponse
{
    public Dictionary<string, List<string>>? Errors { get; set; }
}
