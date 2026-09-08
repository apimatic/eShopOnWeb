using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

/// <summary>
/// The Advanced Billing REST API wraps most single resources in a named object and list
/// resources as arrays of those named objects (e.g. <c>[{"product": {...}}, ...]</c>).
/// These envelopes mirror that wire format.
/// </summary>

public sealed class MaxioCustomerEnvelope
{
    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

public sealed class MaxioProductEnvelope
{
    [JsonPropertyName("product")]
    public MaxioProduct Product { get; set; } = new MaxioProduct();
}

public sealed class MaxioSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription Subscription { get; set; } = new MaxioSubscription();
}

/// <summary>Wire envelope for <c>POST /customers.json</c>.</summary>
public sealed class MaxioCustomerWriteEnvelope
{
    [JsonPropertyName("customer")]
    public MaxioNewCustomer Customer { get; set; } = new MaxioNewCustomer();
}

/// <summary>Wire envelope for <c>POST /subscriptions.json</c>.</summary>
public sealed class MaxioSubscriptionWriteEnvelope
{
    [JsonPropertyName("subscription")]
    public MaxioNewSubscription Subscription { get; set; } = new MaxioNewSubscription();
}
