using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal sealed class MaxioCustomerEnvelope
{
    public MaxioCustomerPayload? Customer { get; set; }
}

internal sealed class MaxioCustomerPayload
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

internal sealed class MaxioCreateCustomerRequest
{
    public required MaxioCreateCustomerAttributes Customer { get; set; }

    [JsonPropertyName("uniqueness_token")]
    public string? UniquenessToken { get; set; }
}

internal sealed class MaxioCreateCustomerAttributes
{
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public required string Email { get; set; }
    public required string Reference { get; set; }
}

internal sealed class MaxioProductEnvelope
{
    public MaxioProductPayload? Product { get; set; }
}

internal sealed class MaxioProductPayload
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public string? ArchivedAt { get; set; }
}

internal sealed class MaxioSubscriptionEnvelope
{
    public MaxioSubscriptionPayload? Subscription { get; set; }
}

internal sealed class MaxioSubscriptionPayload
{
    public int Id { get; set; }
    public string? State { get; set; }
    public long ProductPriceInCents { get; set; }
    public string? CurrentPeriodEndsAt { get; set; }
    public string? NextAssessmentAt { get; set; }
    public string? CreatedAt { get; set; }
    public string? Reference { get; set; }
    public MaxioProductPayload? Product { get; set; }
}

internal sealed class MaxioCreateSubscriptionRequest
{
    public required MaxioCreateSubscriptionAttributes Subscription { get; set; }

    [JsonPropertyName("uniqueness_token")]
    public string? UniquenessToken { get; set; }
}

internal sealed class MaxioCreateSubscriptionAttributes
{
    public required string ProductHandle { get; set; }
    public required int CustomerId { get; set; }
    public string? Reference { get; set; }
    public string? PaymentCollectionMethod { get; set; }
}

internal sealed class MaxioErrorResponse
{
    public object? Errors { get; set; }
}
