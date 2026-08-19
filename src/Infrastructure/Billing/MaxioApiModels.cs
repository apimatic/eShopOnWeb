using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

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

internal sealed class CreateCustomerEnvelope
{
    public CreateCustomerPayload Customer { get; set; } = new();
    public string? UniquenessToken { get; set; }
}

internal sealed class CreateCustomerPayload
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
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
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public MaxioProduct? Product { get; set; }
}

internal sealed class CreateSubscriptionEnvelope
{
    public CreateSubscriptionPayload Subscription { get; set; } = new();
    public string? UniquenessToken { get; set; }
}

internal sealed class CreateSubscriptionPayload
{
    public string ProductHandle { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string PaymentCollectionMethod { get; set; } = "remittance";
}

internal sealed class MaxioErrorResponse
{
    [JsonConverter(typeof(MaxioErrorsConverter))]
    public List<string> Errors { get; set; } = new();
}
