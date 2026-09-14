using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Strongly-typed views of the Maxio Billing API JSON payloads (snake_case on the wire).
/// Only the fields this integration consumes are mapped; Maxio returns many more.
/// </summary>
public class MaxioProductFamily
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
}

public class MaxioProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "month";
    public bool RequireCreditCard { get; set; }
    public bool Taxable { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}

public class MaxioCustomer
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
}

public class MaxioSubscription
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public long ProductPriceInCents { get; set; }
    public long BalanceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public MaxioProduct? Product { get; set; }
    public MaxioCustomer? Customer { get; set; }
}

// --- request payloads ---

public class MaxioCreateCustomerRequest
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
}

public class MaxioCreateSubscriptionRequest
{
    /// <summary>The product (plan) API handle to subscribe to.</summary>
    public string ProductHandle { get; set; } = string.Empty;
    /// <summary>The id of an existing Maxio customer.</summary>
    public int CustomerId { get; set; }
    /// <summary>An app-provided reference for the subscription itself (used for idempotent re-subscribes).</summary>
    public string? Reference { get; set; }
    /// <summary>
    /// "remittance" enrolls the subscriber on invoice billing (an invoice is generated and payment
    /// is recorded manually), so a signup does not require a payment method on file. Use
    /// "automatic" when the customer pays with a stored payment profile.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }
    /// <summary>Duplicate-prevention token; identical submissions within 60 minutes are rejected with 409.</summary>
    public string? UniquenessToken { get; set; }
}

// --- response envelopes ---

public class MaxioProductEnvelope
{
    public MaxioProduct? Product { get; set; }
}

public class MaxioCustomerEnvelope
{
    public MaxioCustomer? Customer { get; set; }
}

public class MaxioSubscriptionEnvelope
{
    public MaxioSubscription? Subscription { get; set; }
}

public class MaxioSubscriptionListEnvelope
{
    public List<MaxioSubscriptionEnvelope>? Items { get; set; }
}
