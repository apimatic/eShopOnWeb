using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

// Wire models for the Maxio Advanced Billing REST API.
// Serialized with JsonNamingPolicy.SnakeCaseLower, so e.g. PriceInCents <-> price_in_cents.

public sealed class MaxioProductResponse
{
    public MaxioProduct? Product { get; set; }
}

public sealed class MaxioProduct
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public bool RequireCreditCard { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}

public sealed class MaxioProductFamily
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
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

public sealed class MaxioCreateCustomerRequest
{
    public MaxioCreateCustomer Customer { get; set; } = new();
}

public sealed class MaxioCreateCustomer
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

public sealed class MaxioSubscriptionResponse
{
    public MaxioSubscription? Subscription { get; set; }
}

public sealed class MaxioSubscription
{
    public long Id { get; set; }
    public string? State { get; set; }
    public string? Reference { get; set; }
    public long ProductPriceInCents { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public MaxioProduct? Product { get; set; }
    public MaxioCustomer? Customer { get; set; }
}

public sealed class MaxioCreateSubscriptionRequest
{
    public MaxioCreateSubscription Subscription { get; set; } = new();
}

public sealed class MaxioCreateSubscription
{
    public string ProductHandle { get; set; } = string.Empty;
    public string CustomerReference { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;

    /// <summary>
    /// "remittance" bills by invoice instead of auto-charging a card, which is what allows
    /// signup to succeed for products that do not require a payment method on file.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";
}

public sealed class MaxioErrorResponse
{
    public List<string>? Errors { get; set; }
}
