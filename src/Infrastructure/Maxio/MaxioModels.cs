using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// DTOs mirroring the Maxio Advanced Billing OpenAPI spec (maxio-spec/openapi.yaml).
// Property names are PascalCase; serialization uses JsonNamingPolicy.SnakeCaseLower
// to match the spec's snake_case field names.

public class MaxioCustomer
{
    public long Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Organization { get; set; }
    public string? Reference { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public class MaxioCustomerResponse
{
    public MaxioCustomer? Customer { get; set; }
}

public class MaxioCreateCustomerRequest
{
    public MaxioCreateCustomerRequest(MaxioCustomerAttributes customer)
    {
        Customer = customer;
    }

    public MaxioCustomerAttributes Customer { get; }
}

public class MaxioCustomerAttributes
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Organization { get; set; }
    public string? Reference { get; set; }
}

public class MaxioProductFamily
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
}

public class MaxioProduct
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long? PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}

public class MaxioProductResponse
{
    public MaxioProduct? Product { get; set; }
}

public class MaxioSubscription
{
    public long Id { get; set; }
    public string? State { get; set; }
    public long? BalanceInCents { get; set; }
    public long? TotalRevenueInCents { get; set; }
    public long? ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public bool? CancelAtEndOfPeriod { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? PaymentCollectionMethod { get; set; }
    public string? Currency { get; set; }
    public string? Reference { get; set; }
    public MaxioCustomer? Customer { get; set; }
    public MaxioProduct? Product { get; set; }
}

public class MaxioSubscriptionResponse
{
    public MaxioSubscription? Subscription { get; set; }
}

public class MaxioCreateSubscriptionRequest
{
    public MaxioCreateSubscriptionRequest(MaxioCreateSubscription subscription)
    {
        Subscription = subscription;
    }

    public MaxioCreateSubscription Subscription { get; }
}

public class MaxioCreateSubscription
{
    public string? ProductHandle { get; set; }
    public long? CustomerId { get; set; }
    public string? CustomerReference { get; set; }
    public string? PaymentCollectionMethod { get; set; }
}

/// <summary>Error body shape returned by Maxio on 422 responses (Error-List-Response).</summary>
public class MaxioErrorListResponse
{
    public List<string>? Errors { get; set; }
}
