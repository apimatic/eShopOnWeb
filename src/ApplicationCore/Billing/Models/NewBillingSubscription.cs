namespace Microsoft.eShopWeb.ApplicationCore.Billing.Models;

/// <summary>
/// Data for enrolling a customer in a plan. The core values are stable, deterministic
/// inputs so repeated subscribe attempts resolve to a single Maxio subscription.
/// </summary>
public record NewBillingSubscription
{
    /// <summary>API handle of the plan (Maxio product) to subscribe to.</summary>
    public required string PlanHandle { get; init; }

    /// <summary>Reference of an existing customer (the eShopOnWeb user identity).</summary>
    public required string CustomerReference { get; init; }

    /// <summary>Application-unique reference for this subscription, e.g. "eshop:{user}:{plan}".</summary>
    public required string Reference { get; init; }

    /// <summary>
    /// Optional payment collection method for the enrollment ("automatic", "remittance", ...).
    /// Use "remittance" for plans that do not require a payment method, so the subscription
    /// activates without attempting to capture a card payment (spec: Collection-Method).
    /// </summary>
    public string? PaymentCollectionMethod { get; init; }
}
