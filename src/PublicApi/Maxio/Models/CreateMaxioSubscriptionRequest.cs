namespace Microsoft.eShopWeb.PublicApi.Maxio.Models;

/// <summary>
/// Request body for POST /subscriptions.json (maxio-spec/components/schemas/Create-Subscription.yaml).
/// Identifies the customer by reference (rather than customer_id) so subscription creation
/// composes directly with the idempotent "ensure customer" step.
/// </summary>
public class CreateMaxioSubscriptionRequest
{
    public string ProductHandle { get; set; } = string.Empty;
    public string CustomerReference { get; set; } = string.Empty;

    /// <summary>
    /// maxio-spec/components/schemas/Collection-Method.yaml. eShopOnWeb's seeded plans are
    /// configured with no required payment method, so subscriptions are created as
    /// "remittance" (invoiced, no card capture/3-DS) rather than the "automatic" default,
    /// which would otherwise try to charge a payment profile that doesn't exist.
    /// </summary>
    public string PaymentCollectionMethod { get; set; } = "remittance";
}

public class CreateMaxioSubscriptionEnvelope
{
    public CreateMaxioSubscriptionRequest Subscription { get; set; } = new();
}
