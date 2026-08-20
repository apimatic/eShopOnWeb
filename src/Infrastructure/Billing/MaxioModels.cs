using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal sealed class MaxioCustomer
{
    public int Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Reference { get; set; }
}

internal sealed class MaxioCustomerEnvelope
{
    public MaxioCustomer? Customer { get; set; }
}

internal sealed class MaxioProductFamily
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
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
    public MaxioProductFamily? ProductFamily { get; set; }
}

internal sealed class MaxioProductEnvelope
{
    public MaxioProduct? Product { get; set; }
}

internal sealed class MaxioSubscription
{
    public int Id { get; set; }
    public string? State { get; set; }
    public string? Reference { get; set; }
    public long ProductPriceInCents { get; set; }
    public System.DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public System.DateTimeOffset? NextAssessmentAt { get; set; }
    public MaxioProduct? Product { get; set; }
    public MaxioCustomer? Customer { get; set; }
}

internal sealed class MaxioSubscriptionEnvelope
{
    public MaxioSubscription? Subscription { get; set; }
}

internal sealed class MaxioErrorPayload
{
    [JsonConverter(typeof(MaxioErrorsConverter))]
    public List<string>? Errors { get; set; }
}
