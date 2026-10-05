using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Shared serializer settings for Billing API wire models. The Billing API uses
/// snake_case property names; .NET naming conventions are used everywhere else.
/// </summary>
public static class MaxioJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

/// <summary>
/// A customer record in Billing API.
/// </summary>
public class MaxioCustomer
{
    public int Id { get; set; }

    public string? Reference { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? Email { get; set; }

    public string? Organization { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>
/// A product (subscription plan) inside a product family.
/// </summary>
public class MaxioProduct
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public string? Handle { get; set; }

    public string? Description { get; set; }

    public long PriceInCents { get; set; }

    public int Interval { get; set; }

    public string? IntervalUnit { get; set; }

    public int? TrialInterval { get; set; }

    public string? TrialIntervalUnit { get; set; }

    public bool RequireCreditCard { get; set; }

    public bool Taxable { get; set; }

    public DateTimeOffset? ArchivedAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public MaxioProductFamilyRef? ProductFamily { get; set; }
}

/// <summary>
/// A product family reference, embedded in product responses.
/// </summary>
public class MaxioProductFamilyRef
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public string? Handle { get; set; }
}

/// <summary>
/// A subscription in Billing API.
/// </summary>
public class MaxioSubscription
{
    public int Id { get; set; }

    public string? State { get; set; }

    public int CustomerId { get; set; }

    public int? ProductId { get; set; }

    public long? ProductPriceInCents { get; set; }

    public long? BalanceInCents { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public DateTimeOffset? NextAssessmentAt { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public bool? CancelAtEndOfPeriod { get; set; }

    public string? PaymentCollectionMethod { get; set; }

    public MaxioProduct? Product { get; set; }

    public MaxioCustomer? Customer { get; set; }

    /// <summary>
    /// True when the subscription is in a state that represents a live (billable or
    /// dunning) relationship, i.e. it has not been canceled or expired.
    /// </summary>
    public bool IsLive()
    {
        var state = State?.ToLowerInvariant();
        return state is not ("canceled" or "expired");
    }
}