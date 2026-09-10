using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

// Wire models mirroring the Maxio (Chargify) JSON API. Only the fields the integration
// consumes are mapped. Property names are PascalCase and translated to/from snake_case by the
// shared JsonSerializerOptions (SnakeCaseLower naming policy) configured on the HTTP client.

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
    public DateTimeOffset? ArchivedAt { get; set; }
    public bool RequireCreditCard { get; set; }
}

internal sealed class MaxioCustomerEnvelope
{
    public MaxioCustomer? Customer { get; set; }
}

internal sealed class MaxioCustomer
{
    public long Id { get; set; }
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
    public long Id { get; set; }
    public string? State { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public MaxioSubscriptionProduct? Product { get; set; }
}

internal sealed class MaxioSubscriptionProduct
{
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
}

/// <summary>
/// Maxio returns validation errors either as an array of strings (<c>{ "errors": [...] }</c>)
/// or, for some endpoints, as an object keyed by field. This converter flattens both shapes
/// into a list of strings so error handling is uniform.
/// </summary>
internal sealed class MaxioErrorEnvelope
{
    [JsonConverter(typeof(MaxioErrorsConverter))]
    public List<string> Errors { get; set; } = new();
}
