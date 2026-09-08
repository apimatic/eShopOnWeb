using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions.Maxio;

internal static class MaxioJson
{
    public static readonly JsonSerializerOptions Default = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public class MaxioCustomer
{
    public long? Id { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? Email { get; set; }

    public string? Reference { get; set; }

    public string? Organization { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}

public class MaxioProductFamily
{
    public long? Id { get; set; }

    public string? Name { get; set; }

    public string? Handle { get; set; }
}

public class MaxioProduct
{
    public long? Id { get; set; }

    public string? Name { get; set; }

    public string? Handle { get; set; }

    public string? Description { get; set; }

    public long? PriceInCents { get; set; }

    public int? Interval { get; set; }

    public string? IntervalUnit { get; set; }

    public bool RequireCreditCard { get; set; }

    public bool Taxable { get; set; }

    public bool InitialChargeAfterTrial { get; set; }

    public long? InitialChargeInCents { get; set; }

    public long? TrialPriceInCents { get; set; }

    public int? TrialInterval { get; set; }

    public string? TrialIntervalUnit { get; set; }

    public DateTimeOffset? ArchivedAt { get; set; }

    public MaxioProductFamily? ProductFamily { get; set; }
}

public class MaxioSubscription
{
    public long? Id { get; set; }

    public string? State { get; set; }

    public long? BalanceInCents { get; set; }

    public long? TotalRevenueInCents { get; set; }

    public long? ProductPriceInCents { get; set; }

    public string? Currency { get; set; }

    public string? Reference { get; set; }

    public string? PaymentCollectionMethod { get; set; }

    public DateTimeOffset? TrialStartedAt { get; set; }

    public DateTimeOffset? TrialEndedAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public DateTimeOffset? NextAssessmentAt { get; set; }

    public MaxioProduct? Product { get; set; }

    public MaxioCustomer? Customer { get; set; }
}

public class MaxioCustomerWrite
{
    public string FirstName { get; set; }

    public string LastName { get; set; }

    public string Email { get; set; }

    public string? Reference { get; set; }

    public string? Organization { get; set; }
}

public class MaxioSubscriptionWrite
{
    public string ProductHandle { get; set; }

    public string CustomerReference { get; set; }

    public DateTimeOffset? NextBillingAt { get; set; }
}

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
