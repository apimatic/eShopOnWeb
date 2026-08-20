using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

internal sealed class MaxioCustomerEnvelope
{
    public MaxioCustomerJson? Customer { get; set; }
}

internal sealed class MaxioCustomerJson
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

internal sealed class MaxioCreateCustomerEnvelope
{
    public MaxioCreateCustomerJson Customer { get; set; } = new();
    public string? UniquenessToken { get; set; }
}

internal sealed class MaxioCreateCustomerJson
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

internal sealed class MaxioProductEnvelope
{
    public MaxioProductJson? Product { get; set; }
}

internal sealed class MaxioProductJson
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
}

internal sealed class MaxioSubscriptionEnvelope
{
    public MaxioSubscriptionJson? Subscription { get; set; }
}

internal sealed class MaxioSubscriptionJson
{
    public int Id { get; set; }
    public string? State { get; set; }
    public long ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public string? Reference { get; set; }
    public MaxioProductJson? Product { get; set; }
}

internal sealed class MaxioCreateSubscriptionEnvelope
{
    public MaxioCreateSubscriptionJson Subscription { get; set; } = new();
    public string? UniquenessToken { get; set; }
}

internal sealed class MaxioCreateSubscriptionJson
{
    public int CustomerId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    /// <summary>
    /// Remittance (invoice) collection so enrollment succeeds when the product
    /// does not require a payment method. Automatic collection would try to
    /// capture the first period immediately and fail without a card on file.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";
}

internal sealed class MaxioErrorPayload
{
    [JsonConverter(typeof(MaxioErrorsConverter))]
    public string? Errors { get; set; }
}
