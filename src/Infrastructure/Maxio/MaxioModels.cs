using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Data shapes exchanged with the Maxio Advanced Billing API.
/// Every property corresponds to a field defined in the Maxio Advanced Billing
/// OpenAPI specification (maxio-spec/openapi.yaml). JSON is serialized with a
/// snake_case naming policy to match the wire format of the spec.
/// </summary>
public sealed class MaxioProduct
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public int? TrialInterval { get; set; }
    public string? TrialIntervalUnit { get; set; }
    public int? TrialPriceInCents { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public bool RequireCreditCard { get; set; }
    public bool Taxable { get; set; }
    public string? ProductPricePointName { get; set; }
    public MaxioProductFamily ProductFamily { get; set; } = new();
}

public sealed class MaxioProductFamily
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
}

public sealed class MaxioCustomer
{
    public long Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class MaxioSubscription
{
    public long Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public int BalanceInCents { get; set; }
    public int ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string PaymentCollectionMethod { get; set; } = string.Empty;
    public MaxioCustomer Customer { get; set; } = new();
    public MaxioProduct Product { get; set; } = new();
}

/// <summary>Envelope for a single Product ("product") response from the API.</summary>
public sealed class MaxioProductResponse
{
    public MaxioProduct Product { get; set; } = new();
}

/// <summary>Envelope for a single Customer ("customer") response from the API.</summary>
public sealed class MaxioCustomerResponse
{
    public MaxioCustomer Customer { get; set; } = new();
}

/// <summary>Envelope for a single Subscription ("subscription") response from the API.</summary>
public sealed class MaxioSubscriptionResponse
{
    public MaxioSubscription Subscription { get; set; } = new();
}

public sealed class CreateMaxioCustomerRequest
{
    public MaxioCreateCustomer Customer { get; set; } = new();
}

public sealed class MaxioCreateCustomer
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
}

public sealed class CreateMaxioSubscriptionRequest
{
    public MaxioCreateSubscription Subscription { get; set; } = new();
}

public sealed class MaxioCreateSubscription
{
    public string? ProductHandle { get; set; }
    public long? CustomerId { get; set; }
    public string? Reference { get; set; }
    public string? CustomerReference { get; set; }

    /// <summary>
    /// "remittance" (per the spec's Collection-Method schema) lets a subscription be
    /// created without a stored payment method: no charge is attempted at signup and
    /// the customer is billed on receipt — the mode the seeded plans are configured for.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }
}