using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

// Data-transfer objects mirroring the Maxio Advanced Billing JSON payloads.
// Property names map to snake_case via the shared serializer options (see MaxioJson).
// Only the fields the integration needs are modelled.

internal sealed class MaxioProductEnvelope
{
    public MaxioProduct? Product { get; set; }
}

internal sealed class MaxioProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public bool RequireCreditCard { get; set; }
    public bool Taxable { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
}

internal sealed class MaxioCustomerEnvelope
{
    public MaxioCustomer? Customer { get; set; }
}

internal sealed class MaxioCustomer
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

internal sealed class MaxioSubscriptionEnvelope
{
    public MaxioSubscription? Subscription { get; set; }
}

internal sealed class MaxioSubscription
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public long ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public MaxioProduct? Product { get; set; }
    public MaxioCustomer? Customer { get; set; }
}

// ---- Request payloads ----

internal sealed class CreateCustomerRequest
{
    public CreateCustomerBody Customer { get; set; } = new();
}

internal sealed class CreateCustomerBody
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

internal sealed class CreateSubscriptionRequest
{
    public CreateSubscriptionBody Subscription { get; set; } = new();

    /// <summary>
    /// Guards against duplicate submissions: a repeated create with the same token within the
    /// provider's window is rejected with 409 instead of creating a second subscription.
    /// </summary>
    public string UniquenessToken { get; set; } = string.Empty;
}

internal sealed class CreateSubscriptionBody
{
    public string ProductHandle { get; set; } = string.Empty;
    public int CustomerId { get; set; }

    /// <summary>Our own reference for the subscription, for traceability in the Maxio UI.</summary>
    public string? Reference { get; set; }

    /// <summary>
    /// How payment is collected. "remittance" (invoice billing) issues an invoice at renewal
    /// instead of auto-charging a card, so subscriptions to plans that do not require a payment
    /// method can be created without capturing card details.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }
}

// ---- Error payload ----

internal sealed class MaxioErrorEnvelope
{
    // Maxio may return errors either as an array of strings or as an object keyed by field.
    [JsonPropertyName("errors")]
    public List<string>? Errors { get; set; }
}
