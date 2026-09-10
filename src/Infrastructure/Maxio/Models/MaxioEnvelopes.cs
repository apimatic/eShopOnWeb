namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

// Maxio wraps single resources in a named envelope, e.g. { "customer": { ... } }, and
// returns collections as arrays of those envelopes, e.g. [ { "product": { ... } }, ... ].

internal sealed class MaxioProductFamilyEnvelope
{
    public MaxioProductFamily? ProductFamily { get; set; }
}

internal sealed class MaxioProductEnvelope
{
    public MaxioProduct? Product { get; set; }
}

internal sealed class MaxioCustomerEnvelope
{
    public MaxioCustomer? Customer { get; set; }
}

internal sealed class MaxioSubscriptionEnvelope
{
    public MaxioSubscription? Subscription { get; set; }
}

/// <summary>Shape of a Maxio error response: <c>{ "errors": [ "message", ... ] }</c>.</summary>
internal sealed class MaxioErrorEnvelope
{
    public System.Collections.Generic.List<string>? Errors { get; set; }
}
