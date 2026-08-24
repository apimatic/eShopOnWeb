using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// Wire DTOs for the Maxio Advanced Billing API. Serialized with a snake_case naming policy,
// so property names here map to the API's snake_case JSON fields.

public sealed class MaxioProductResponse
{
    public MaxioProduct? Product { get; set; }
}

public sealed class MaxioProduct
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

public sealed class MaxioCustomerResponse
{
    public MaxioCustomer? Customer { get; set; }
}

public sealed class MaxioCustomer
{
    public long Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

public sealed class MaxioSubscriptionResponse
{
    public MaxioSubscription? Subscription { get; set; }
}

public sealed class MaxioSubscription
{
    public long Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public MaxioProduct? Product { get; set; }
    public MaxioCustomer? Customer { get; set; }
}

public sealed class CreateCustomerRequest
{
    public CreateCustomerAttributes Customer { get; set; } = new();
}

public sealed class CreateCustomerAttributes
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
}

public sealed class CreateSubscriptionRequest
{
    public CreateSubscriptionAttributes Subscription { get; set; } = new();
}

public sealed class CreateSubscriptionAttributes
{
    public string? ProductHandle { get; set; }
    public long? CustomerId { get; set; }
    public string? Reference { get; set; }
    public string? PaymentCollectionMethod { get; set; }
}

public sealed class MaxioErrorResponse
{
    public List<string>? Errors { get; set; }
}
