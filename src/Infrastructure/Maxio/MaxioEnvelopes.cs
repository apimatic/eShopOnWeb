namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Response envelope returned by Maxio endpoints that return a single customer
/// (<c>Customer-Response.yaml</c> in the Maxio spec).
/// </summary>
public sealed class MaxioCustomerEnvelope
{
    public MaxioCustomer? Customer { get; set; }
}

/// <summary>
/// Response envelope returned by Maxio endpoints that return a single product
/// (<c>Product-Response.yaml</c> in the Maxio spec).
/// </summary>
public sealed class MaxioProductEnvelope
{
    public MaxioProduct? Product { get; set; }
}

/// <summary>
/// Response envelope returned by Maxio endpoints that return a single product family
/// (<c>Product-Family-Response.yaml</c> in the Maxio spec).
/// </summary>
public sealed class MaxioProductFamilyEnvelope
{
    public MaxioProductFamily? ProductFamily { get; set; }
}

/// <summary>
/// Response envelope returned by Maxio endpoints that return a single subscription
/// (<c>Subscription-Response.yaml</c> in the Maxio spec).
/// </summary>
public sealed class MaxioSubscriptionEnvelope
{
    public MaxioSubscription? Subscription { get; set; }
}
