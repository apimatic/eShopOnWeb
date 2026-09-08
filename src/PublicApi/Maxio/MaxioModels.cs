using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// POCOs that mirror the JSON payloads exchanged with the Maxio Advanced Billing API.
/// Property names are PascalCase in code; serialization maps them to snake_case.
/// </summary>
public sealed class MaxioProductFamily
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
}

public sealed class MaxioProduct
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long? PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public bool RequireCreditCard { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}

public sealed class MaxioCustomer
{
    public long Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

public sealed class MaxioSubscription
{
    public long Id { get; set; }
    public string? State { get; set; }
    public long? BalanceInCents { get; set; }
    public long? ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public string? PaymentCollectionMethod { get; set; }
    public MaxioProduct? Product { get; set; }
    public MaxioCustomer? Customer { get; set; }
}

/// <summary>Fields used to create a Maxio customer.</summary>
public sealed class MaxioCustomerDraft
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

/// <summary>Fields used to subscribe an existing customer (by reference) to a product.</summary>
public sealed class MaxioSubscriptionDraft
{
    public string? ProductHandle { get; set; }
    public string? CustomerReference { get; set; }
    public string? PaymentCollectionMethod { get; set; }
}

internal sealed class ProductEnvelope
{
    public MaxioProduct? Product { get; set; }
}

internal sealed class CustomerEnvelope
{
    public MaxioCustomer? Customer { get; set; }
}

internal sealed class SubscriptionEnvelope
{
    public MaxioSubscription? Subscription { get; set; }
}

internal sealed class CreateCustomerBody
{
    public MaxioCustomerDraft? Customer { get; set; }
}

internal sealed class CreateSubscriptionBody
{
    public MaxioSubscriptionDraft? Subscription { get; set; }
}

internal sealed class MaxioErrorsBody
{
    public System.Collections.Generic.List<string>? Errors { get; set; }
}

internal static class MaxioJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };
}
