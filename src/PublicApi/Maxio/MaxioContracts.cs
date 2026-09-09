using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

// Raw wire contracts for the Maxio Advanced Billing (Billing API) JSON endpoints.
// The API serializes all properties in snake_case; the client applies a snake_case
// naming policy so these CLR names map 1:1 onto the wire format.

public sealed class MaxioProductWrapper
{
    public MaxioProduct Product { get; set; } = new();
}

public sealed class MaxioCustomerWrapper
{
    public MaxioCustomer Customer { get; set; } = new();
}

public sealed class MaxioSubscriptionWrapper
{
    public MaxioSubscription Subscription { get; set; } = new();
}

public sealed class MaxioProductFamilyRef
{
    public int Id { get; set; }

    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

public sealed class MaxioProduct
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Handle { get; set; }

    public string? Description { get; set; }

    public long PriceInCents { get; set; }

    public int Interval { get; set; }

    public string IntervalUnit { get; set; } = string.Empty;

    public int? TrialInterval { get; set; }

    public string? TrialIntervalUnit { get; set; }

    public bool RequireCreditCard { get; set; }

    public bool Taxable { get; set; }

    public DateTime? ArchivedAt { get; set; }

    public MaxioProductFamilyRef? ProductFamily { get; set; }
}

public sealed class MaxioCustomer
{
    public int Id { get; set; }

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? Reference { get; set; }
}

public sealed class MaxioSubscription
{
    public int Id { get; set; }

    public string State { get; set; } = string.Empty;

    public MaxioProduct? Product { get; set; }

    public long ProductPriceInCents { get; set; }

    public DateTime? CurrentPeriodEndsAt { get; set; }

    public DateTime? NextAssessmentAt { get; set; }

    public DateTime? ActivatedAt { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? TrialEndedAt { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public bool? CancelAtEndOfPeriod { get; set; }

    public MaxioCustomer? Customer { get; set; }
}

public sealed class MaxioCreateCustomerRequest
{
    public MaxioCreateCustomerAttributes Customer { get; set; } = new();
}

public sealed class MaxioCreateCustomerAttributes
{
    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? Reference { get; set; }
}

public sealed class MaxioCreateSubscriptionRequest
{
    public MaxioCreateSubscriptionAttributes Subscription { get; set; } = new();
}

public sealed class MaxioCreateSubscriptionAttributes
{
    public string ProductHandle { get; set; } = string.Empty;

    public int CustomerId { get; set; }

    /// <summary>
    /// Remittance billing: invoices are generated at renewal and paid manually rather than
    /// charged to a stored payment method. This is what allows signups on products that do
    /// not require a card (no payment profile captured in this integration).
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";
}

public static class MaxioSubscriptionStates
{
    /// <summary>States that represent a live (non end-of-life) subscription.</summary>
    public static readonly IReadOnlySet<string> LiveStates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "pending", "awaiting_signup", "trialing", "assessing", "active",
        "soft_failure", "past_due", "suspended", "unpaid", "on_hold", "paused"
    };
}
