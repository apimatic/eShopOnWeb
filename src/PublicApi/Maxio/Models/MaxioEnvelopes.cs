namespace Microsoft.eShopWeb.PublicApi.Maxio.Models;

/// <summary>
/// A Maxio Customer envelope used by single-resource responses such as Create Customer and
/// Read Customer by Reference. Wire shape: <c>{ "customer": { ... } }</c>.
/// </summary>
public class MaxioCustomerEnvelope
{
    public MaxioCustomer? Customer { get; set; }
}

/// <summary>
/// A Maxio Product envelope used by list responses such as List Products for Product Family.
/// Wire shape: <c>[ { "product": { ... } } ]</c>.
/// </summary>
public class MaxioProductEnvelope
{
    public MaxioProduct? Product { get; set; }
}

/// <summary>
/// A Maxio Subscription envelope used by create/read/list-subscription responses.
/// Wire shape: <c>{ "subscription": { ... } }</c> or <c>[ { "subscription": { ... } } ]</c>.
/// </summary>
public class MaxioSubscriptionEnvelope
{
    public MaxioSubscription? Subscription { get; set; }
}
