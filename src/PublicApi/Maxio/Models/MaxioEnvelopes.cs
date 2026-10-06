using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Maxio.Models;

/// <summary>Envelope for a single customer response: { "customer": { ... } }.</summary>
public class MaxioCustomerEnvelope
{
    public MaxioCustomer? Customer { get; set; }
}

/// <summary>Envelope for a single product response: { "product": { ... } }.</summary>
public class MaxioProductEnvelope
{
    public MaxioProduct? Product { get; set; }
}

/// <summary>Envelope for a single subscription response: { "subscription": { ... } }.</summary>
public class MaxioSubscriptionEnvelope
{
    public MaxioSubscription? Subscription { get; set; }
}

/// <summary>Envelope for an error response: { "errors": [...] }.</summary>
public class MaxioErrorEnvelope
{
    public List<string>? Errors { get; set; }
}
