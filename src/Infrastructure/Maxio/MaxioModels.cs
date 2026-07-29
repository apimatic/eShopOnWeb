using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// Internal wire models for the Maxio Advanced Billing REST API. Only the fields the
// integration consumes are declared. Property names map to Maxio's snake_case JSON via a
// SnakeCaseLower naming policy configured on the serializer (see MaxioApiClient).

internal sealed class MaxioCustomerEnvelope
{
    public MaxioCustomer? Customer { get; set; }
}

internal sealed class MaxioCustomer
{
    public long Id { get; set; }
    public string? Reference { get; set; }
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

internal sealed class MaxioProductEnvelope
{
    public MaxioProduct? Product { get; set; }
}

internal sealed class MaxioProduct
{
    public long Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
}

internal sealed class MaxioSubscriptionEnvelope
{
    public MaxioSubscription? Subscription { get; set; }
}

internal sealed class MaxioSubscription
{
    public long Id { get; set; }
    public string? State { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public int ProductPriceInCents { get; set; }
    public string? PaymentCollectionMethod { get; set; }
    public MaxioProduct? Product { get; set; }
}

// ----- Request models -----

internal sealed class CreateCustomerRequest
{
    public CreateCustomerBody Customer { get; set; } = new();

    /// <summary>Idempotency guard: identical POSTs within 60 minutes are rejected as duplicates.</summary>
    public string? UniquenessToken { get; set; }
}

internal sealed class CreateCustomerBody
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

internal sealed class CreateSubscriptionRequest
{
    public CreateSubscriptionBody Subscription { get; set; } = new();

    /// <summary>Idempotency guard: identical POSTs within 60 minutes are rejected as duplicates.</summary>
    public string? UniquenessToken { get; set; }
}

internal sealed class CreateSubscriptionBody
{
    public string? ProductHandle { get; set; }
    public long CustomerId { get; set; }
    public string? PaymentCollectionMethod { get; set; }
}
