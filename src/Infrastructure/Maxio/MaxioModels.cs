using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// ---------------------------------------------------------------------------
// Wire models for the Maxio Advanced Billing (formerly Chargify) JSON API.
// All request/response property names follow Maxio's snake_case convention via
// JsonNamingPolicy.SnakeCaseLower on the serializer. Every shape used here was
// verified against a live Maxio Advanced Billing sandbox site.
// ---------------------------------------------------------------------------

#region Requests

/// <summary>Payload for POST /customers.json ("customer" wrapper applied by the client).</summary>
public sealed class MaxioCreateCustomerRequest
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    /// <summary>Caller-defined unique identifier used for idempotent customer lookups.</summary>
    public string Reference { get; set; } = string.Empty;
}

/// <summary>Payload for POST /subscriptions.json ("subscription" wrapper applied by the client).</summary>
public sealed class MaxioCreateSubscriptionRequest
{
    public int CustomerId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    /// <summary>"remittance" allows subscribing without capturing a payment method (verified in sandbox).</summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";
}

#endregion

#region Responses

public sealed class MaxioCustomer
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
}

public sealed class MaxioProductFamily
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class MaxioProduct
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "month";
    public bool RequireCreditCard { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}

/// <summary>
/// Subscription summary as returned by create/read/list subscription endpoints.
/// The embedded "product" and "customer" objects use a reduced shape on list endpoints,
/// so only fields present across all shapes are mapped.
/// </summary>
public sealed class MaxioSubscription
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string Currency { get; set; } = "USD";
    public int BalanceInCents { get; set; }
    public int ProductPriceInCents { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? CanceledAt { get; set; }
    public string? Reference { get; set; }
    public MaxioSubscriptionProduct? Product { get; set; }
    public MaxioSubscriptionCustomer? Customer { get; set; }
}

public sealed class MaxioSubscriptionProduct
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "month";
}

public sealed class MaxioSubscriptionCustomer
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string? Email { get; set; }
}

#endregion

#region Envelope wrappers (Maxio wraps single objects and list entries)

public sealed class MaxioCustomerEnvelope
{
    public MaxioCustomer? Customer { get; set; }
}

public sealed class MaxioProductFamilyEnvelope
{
    public MaxioProductFamily? ProductFamily { get; set; }
}

public sealed class MaxioProductEnvelope
{
    public MaxioProduct? Product { get; set; }
}

public sealed class MaxioSubscriptionEnvelope
{
    public MaxioSubscription? Subscription { get; set; }
}

/// <summary>Error body shape: {"errors": ["message", ...]}</summary>
public sealed class MaxioErrorEnvelope
{
    public List<string> Errors { get; set; } = new();
}

#endregion
