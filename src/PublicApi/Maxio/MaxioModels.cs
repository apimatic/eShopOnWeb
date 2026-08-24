using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

// Deserialization models for the Maxio Billing API. Property names are mapped
// from Maxio's snake_case JSON via JsonNamingPolicy.SnakeCaseLower.

public class MaxioCustomer
{
    public long Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

public class MaxioProductFamily
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
}

public class MaxioProduct
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}

public class MaxioSubscription
{
    public long Id { get; set; }
    public string? State { get; set; }
    public string? Reference { get; set; }
    public long ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public MaxioProduct? Product { get; set; }
    public MaxioCustomer? Customer { get; set; }
}

// Request/response envelopes used by the Billing API.

public class MaxioCustomerEnvelope
{
    public MaxioCustomer? Customer { get; set; }
}

public class MaxioProductEnvelope
{
    public MaxioProduct? Product { get; set; }
}

public class MaxioSubscriptionEnvelope
{
    public MaxioSubscription? Subscription { get; set; }
}

public class MaxioCreateCustomerRequest
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

public class MaxioCreateCustomerEnvelope
{
    public MaxioCreateCustomerRequest? Customer { get; set; }
}

public class MaxioCreateSubscriptionRequest
{
    public string? ProductHandle { get; set; }
    public string? CustomerReference { get; set; }
    public string? Reference { get; set; }

    /// <summary>"remittance" bills by invoice instead of auto-charging a card on file.</summary>
    public string? PaymentCollectionMethod { get; set; }
}

public class MaxioCreateSubscriptionEnvelope
{
    public MaxioCreateSubscriptionRequest? Subscription { get; set; }
}
