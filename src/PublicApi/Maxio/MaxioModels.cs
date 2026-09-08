using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

// Wire models for the Maxio Advanced Billing REST API. The property names map
// to the snake_case JSON members defined by the Maxio OpenAPI specification in
// maxio-spec/ via JsonNamingPolicy.SnakeCaseLower. Only the subset of the
// specification needed by the subscription capability is modelled here.

/// <summary>Customer object returned by Maxio (Customer schema).</summary>
public class MaxioCustomer
{
    public long? Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Organization { get; set; }
    public string? Reference { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>Payload used to create a customer (Create-Customer schema).</summary>
public class MaxioCustomerProfile
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Organization { get; set; }
    public string? Reference { get; set; }
}

/// <summary>Product family object (Product-Family schema).</summary>
public class MaxioProductFamily
{
    public long? Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
}

/// <summary>Product ("plan") object (Product schema).</summary>
public class MaxioProduct
{
    public long? Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long? PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public long? InitialChargeInCents { get; set; }
    public long? TrialPriceInCents { get; set; }
    public int? TrialInterval { get; set; }
    public string? TrialIntervalUnit { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public bool? RequireCreditCard { get; set; }
    public bool? RequestCreditCard { get; set; }
    public bool? Taxable { get; set; }
    public string? ProductPricePointName { get; set; }
    public int? VersionNumber { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}

/// <summary>Subscription object (Subscription schema).</summary>
public class MaxioSubscription
{
    public long? Id { get; set; }
    public string? State { get; set; }
    public long? BalanceInCents { get; set; }
    public long? ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? TrialStartedAt { get; set; }
    public DateTimeOffset? TrialEndedAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public bool? CancelAtEndOfPeriod { get; set; }
    public string? CancellationMessage { get; set; }
    public string? PaymentCollectionMethod { get; set; }
    public string? SignupRevenue { get; set; }
    public string? Currency { get; set; }
    public MaxioCustomer? Customer { get; set; }
    public MaxioProduct? Product { get; set; }
}

// ---- Request envelopes (root element is required by the spec) ----

/// <summary>Request body for POST /customers.json.</summary>
public class MaxioCreateCustomerEnvelope
{
    public MaxioCustomerProfile? Customer { get; set; }
}

/// <summary>Request body for POST /subscriptions.json (Create-Subscription schema).</summary>
public class MaxioCreateSubscriptionEnvelope
{
    public MaxioCreateSubscriptionData? Subscription { get; set; }
}

public class MaxioCreateSubscriptionData
{
    public string? ProductHandle { get; set; }
    public long? CustomerId { get; set; }

    /// <summary>
    /// See Collection-Method schema. "remittance" invoices instead of auto-charging,
    /// so a shopper can subscribe to a plan without providing a payment method.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }
}

// ---- Response envelopes (the API wraps resources in a named object) ----

public class MaxioCustomerEnvelope
{
    public MaxioCustomer? Customer { get; set; }
}

public class MaxioProductEnvelope
{
    public MaxioProduct? Product { get; set; }
}

public class MaxioProductFamilyEnvelope
{
    public MaxioProductFamily? ProductFamily { get; set; }
}

public class MaxioSubscriptionEnvelope
{
    public MaxioSubscription? Subscription { get; set; }
}
