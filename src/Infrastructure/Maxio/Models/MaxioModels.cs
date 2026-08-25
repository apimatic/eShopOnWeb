using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

// DTOs for the Maxio Advanced Billing REST API. Serialized with SnakeCaseLower naming,
// matching the wire format verified against the live API (e.g. "price_in_cents").

public class MaxioProductFamilyEnvelope
{
    public MaxioProductFamily? ProductFamily { get; set; }
}

public class MaxioProductFamily
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Handle { get; set; }
}

public class MaxioProductEnvelope
{
    public MaxioProduct? Product { get; set; }
}

public class MaxioProduct
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

public class MaxioCustomerEnvelope
{
    public MaxioCustomer? Customer { get; set; }
}

public class MaxioCustomer
{
    public long Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

public class MaxioCreateCustomerRequest
{
    public MaxioCreateCustomer Customer { get; set; } = new();
}

public class MaxioCreateCustomer
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
}

public class MaxioSubscriptionEnvelope
{
    public MaxioSubscription? Subscription { get; set; }
}

public class MaxioSubscription
{
    public long Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public string Currency { get; set; } = string.Empty;
    public long ProductPriceInCents { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public MaxioProduct? Product { get; set; }
    public MaxioCustomer? Customer { get; set; }
}

public class MaxioCreateSubscriptionRequest
{
    public MaxioCreateSubscription Subscription { get; set; } = new();
}

public class MaxioCreateSubscription
{
    public string? ProductHandle { get; set; }
    public long? CustomerId { get; set; }
    public string? CustomerReference { get; set; }
    public string? Reference { get; set; }

    /// <summary>
    /// "remittance" bills by invoice without requiring a payment method on file,
    /// which matches the seeded plans (no card capture / 3-DS).
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";
}

public class MaxioErrorResponse
{
    public List<string>? Errors { get; set; }
}
