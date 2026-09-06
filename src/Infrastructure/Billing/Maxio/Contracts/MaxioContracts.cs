using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;

// Wire shapes for the Maxio Advanced Billing REST API. Property names are mapped by the client's
// snake_case naming policy; only the fields this integration actually reads are modelled, and
// unknown fields are ignored so that additions on the provider side stay non-breaking.

internal sealed class MaxioProductEnvelope
{
    public MaxioProduct? Product { get; set; }
}

internal sealed class MaxioProduct
{
    public long Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public bool RequireCreditCard { get; set; }
    public bool Taxable { get; set; }
    public long? TrialPriceInCents { get; set; }
    public int? TrialInterval { get; set; }
    public string? TrialIntervalUnit { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}

internal sealed class MaxioProductFamily
{
    public long Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
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

internal sealed class MaxioCreateCustomerRequest
{
    public MaxioCreateCustomerRequest(MaxioCustomerAttributes customer) => Customer = customer;

    public MaxioCustomerAttributes Customer { get; }
}

internal sealed class MaxioCustomerAttributes
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
    public string? Currency { get; set; }
    public long BalanceInCents { get; set; }
    public long ProductPriceInCents { get; set; }
    public string? PaymentCollectionMethod { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? TrialEndedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public MaxioProduct? Product { get; set; }
    public MaxioCustomer? Customer { get; set; }
}

internal sealed class MaxioCreateSubscriptionRequest
{
    public MaxioCreateSubscriptionRequest(MaxioSubscriptionAttributes subscription) => Subscription = subscription;

    public MaxioSubscriptionAttributes Subscription { get; }
}

internal sealed class MaxioSubscriptionAttributes
{
    public string? ProductHandle { get; set; }
    public string? CustomerReference { get; set; }
    public string? Reference { get; set; }
    public string? PaymentCollectionMethod { get; set; }
}

/// <summary>
/// The error envelope Advanced Billing returns with a 422, for example
/// <c>{"errors":["Reference: must be unique - that value has been taken."]}</c>.
/// </summary>
internal sealed class MaxioErrorEnvelope
{
    [JsonPropertyName("errors")]
    public List<string>? Errors { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
