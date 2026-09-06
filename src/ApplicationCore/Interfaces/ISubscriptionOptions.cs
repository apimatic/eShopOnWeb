namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The subscription behaviour the hosting application configures. Declared here so the service
/// does not have to know where the values come from.
/// </summary>
public interface ISubscriptionOptions
{
    /// <summary>Plan handle used when a subscribe request does not name one. May be null.</summary>
    string? DefaultPlanHandle { get; }

    /// <summary>
    /// How the billing system should collect payment for new subscriptions, e.g. "remittance" to
    /// invoice the customer or "automatic" to charge a stored card. Null leaves it to the site default.
    /// </summary>
    string? PaymentCollectionMethod { get; }
}
