using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// A customer in Maxio Advanced Billing. Used for both requests (create) and
/// responses; the serializer omits null/optional members when writing.
/// </summary>
public sealed class MaxioCustomer
{
    public long? Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
    public string? Organization { get; set; }
}

/// <summary>
/// A plan (product) offered by the subscription product family.
/// </summary>
public sealed class MaxioProduct
{
    public long? Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long? PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
}

/// <summary>
/// A subscription in Maxio Advanced Billing. This is the projection the rest of
/// the application consumes; Maxio is the system of record for state.
/// </summary>
public sealed class MaxioSubscription
{
    public long Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string? ProductHandle { get; set; }
    public string? ProductName { get; set; }
    public long? PriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public long CustomerId { get; set; }
}

/// <summary>
/// Payload for POST /subscriptions.json.
/// </summary>
public sealed class MaxioSubscriptionCreate
{
    public long CustomerId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;

    /// <summary>
    /// How the subscription is collected. "remittance" means the subscription is
    /// invoiced rather than auto-charged, which allows signup without a stored
    /// payment method (used by the sandbox catalog, which requires no card).
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }
}

/// <summary>
/// Outcome of an idempotent subscribe operation.
/// </summary>
public sealed record SubscribeResult(MaxioSubscription Subscription, bool AlreadySubscribed);

/// <summary>
/// Thrown when the Maxio API returns a non-success status or the transport layer
/// fails. Carries the upstream status code so callers/middleware can map it.
/// </summary>
public sealed class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, string message, string? errorBody = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorBody = errorBody;
    }

    /// <summary>HTTP status code returned by the Maxio API (or 0 for transport failures).</summary>
    public int StatusCode { get; }

    /// <summary>Raw error body returned by Maxio, when available.</summary>
    public string? ErrorBody { get; }
}

// ---------------------------------------------------------------------------
// Wire envelopes. The Billing API wraps single resources as { "<resource>": ... }
// and list responses as { "items": [ { "<resource>": ... }, ... ] }.
// ---------------------------------------------------------------------------

internal sealed class MaxioCustomerEnvelope
{
    public MaxioCustomer? Customer { get; set; }
}

internal sealed class MaxioProductItem
{
    public MaxioProduct? Product { get; set; }
}

internal sealed class MaxioProductListEnvelope
{
    public List<MaxioProductItem>? Items { get; set; }
}

internal sealed class MaxioSubscriptionPayload
{
    public long? Id { get; set; }
    public string? State { get; set; }
    public long? ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public MaxioCustomerPayload? Customer { get; set; }
    public MaxioProductPayload? Product { get; set; }
}

internal sealed class MaxioCustomerPayload
{
    public long? Id { get; set; }
    public string? Reference { get; set; }
}

internal sealed class MaxioProductPayload
{
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public long? PriceInCents { get; set; }
}

internal sealed class MaxioSubscriptionCreateEnvelope
{
    public MaxioSubscriptionCreate? Subscription { get; set; }

    public string? UniquenessToken { get; set; }
}

internal sealed class MaxioSubscriptionEnvelope
{
    public MaxioSubscriptionPayload? Subscription { get; set; }
}

internal sealed class MaxioSubscriptionItem
{
    public MaxioSubscriptionPayload? Subscription { get; set; }
}

internal sealed class MaxioSubscriptionListEnvelope
{
    public List<MaxioSubscriptionItem>? Items { get; set; }
}
