using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// Wire models for the Maxio (Advanced Billing) REST API. Property names are PascalCase and mapped to
// Maxio's snake_case JSON via a SnakeCaseLower naming policy configured on the JsonSerializerOptions.
// Only the fields eShopOnWeb needs are modelled.

internal sealed class MaxioProductEnvelope
{
    public MaxioProduct? Product { get; set; }
}

internal sealed class MaxioProduct
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public bool RequireCreditCard { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
}

internal sealed class MaxioCustomerEnvelope
{
    public MaxioCustomer? Customer { get; set; }
}

internal sealed class MaxioCustomer
{
    public int Id { get; set; }
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
    public int Id { get; set; }
    public string? State { get; set; }
    public long ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public MaxioProduct? Product { get; set; }
    public MaxioCustomer? Customer { get; set; }
}

// ----- Request bodies -----

internal sealed class CreateCustomerRequest
{
    public CreateCustomerRequest(CustomerAttributes customer) => Customer = customer;

    public CustomerAttributes Customer { get; }
}

internal sealed class CustomerAttributes
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

internal sealed class CreateSubscriptionRequest
{
    public SubscriptionAttributes Subscription { get; set; } = new();

    /// <summary>
    /// Duplicate-prevention token. A repeated create with the same token inside Maxio's 60-minute
    /// window is rejected with 409, guarding against duplicate subscriptions from rapid retries.
    /// </summary>
    public string? UniquenessToken { get; set; }
}

internal sealed class SubscriptionAttributes
{
    public string ProductHandle { get; set; } = string.Empty;
    public int CustomerId { get; set; }

    /// <summary>
    /// How Maxio collects payment. Set to an invoice/remittance method so a paid plan can be started
    /// without a stored payment method (an invoice is generated rather than an auto-charge being attempted).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>Serialized only when set; lets the caller pin a specific plan price point.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProductPricePointHandle { get; set; }
}

// ----- Site -----

internal sealed class MaxioSiteEnvelope
{
    public MaxioSite? Site { get; set; }
}

internal sealed class MaxioSite
{
    public bool RelationshipInvoicingEnabled { get; set; }
}
