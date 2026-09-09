using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Wire models for the Maxio Advanced Billing API, mirroring the schemas of
/// maxio-spec/openapi.yaml (snake_case JSON, { resource: { ... } } envelopes).
/// Only the fields this integration consumes are modeled; unknown fields are ignored.
/// </summary>
internal sealed class MaxioProductFamilyEnvelope
{
    public MaxioProductFamily? ProductFamily { get; set; }
}

internal sealed class MaxioProductFamily
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
}

internal sealed class MaxioProductEnvelope
{
    public MaxioProduct? Product { get; set; }
}

internal sealed class MaxioProduct
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public bool? RequireCreditCard { get; set; }
    public string? ArchivedAt { get; set; }
    public string? ProductPricePointName { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}

internal sealed class MaxioCustomerEnvelope
{
    public MaxioCustomer? Customer { get; set; }
}

internal sealed class MaxioCustomer
{
    public long Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

internal sealed class MaxioCreateCustomerEnvelope
{
    public MaxioCreateCustomer? Customer { get; set; }
}

internal sealed class MaxioCreateCustomer
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

internal sealed class MaxioSubscriptionEnvelope
{
    public MaxioSubscription? Subscription { get; set; }
}

internal sealed class MaxioSubscription
{
    public long Id { get; set; }
    public string? State { get; set; }
    public string? Reference { get; set; }
    public long ProductPriceInCents { get; set; }
    public string? CurrentPeriodEndsAt { get; set; }
    public string? NextAssessmentAt { get; set; }
    public string? ActivatedAt { get; set; }
    public string? CreatedAt { get; set; }
    public MaxioProduct? Product { get; set; }
    public MaxioCustomer? Customer { get; set; }
}

internal sealed class MaxioCreateSubscriptionEnvelope
{
    public MaxioCreateSubscription? Subscription { get; set; }
}

internal sealed class MaxioCreateSubscription
{
    /// <summary>The API handle of the product to subscribe to.</summary>
    public string? ProductHandle { get; set; }

    public long? CustomerId { get; set; }

    /// <summary>Application-provided unique reference for the subscription itself.</summary>
    public string? Reference { get; set; }

    /// <summary>"remittance" (invoice billing) - lets card-less plans activate without a payment profile.</summary>
    public string? PaymentCollectionMethod { get; set; }
}
