using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

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
}

internal sealed class MaxioCustomerEnvelope
{
    public MaxioCustomerPayload? Customer { get; set; }
}

internal sealed class MaxioCustomerPayload
{
    public int Id { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

internal sealed class MaxioCreateCustomerRequest
{
    public MaxioCreateCustomerBody Customer { get; set; } = new();
}

internal sealed class MaxioCreateCustomerBody
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public string? Organization { get; set; }
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
    public MaxioProduct? Product { get; set; }
}

internal sealed class MaxioCreateSubscriptionRequest
{
    public MaxioCreateSubscriptionBody Subscription { get; set; } = new();
    [JsonPropertyName("uniqueness_token")]
    public string? UniquenessToken { get; set; }
}

internal sealed class MaxioCreateSubscriptionBody
{
    public string? ProductHandle { get; set; }
    public int CustomerId { get; set; }
    public string? Reference { get; set; }
    public string? PaymentCollectionMethod { get; set; }
}

internal sealed class MaxioErrorResponse
{
    public object? Errors { get; set; }
}
