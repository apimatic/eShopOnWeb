namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

/// <summary>
/// Spec schema "Create-Subscription-Request".
/// </summary>
public class MaxioCreateSubscriptionRequest
{
    public MaxioCreateSubscription Subscription { get; set; } = new();
}

/// <summary>
/// Spec schema "Create-Subscription". Only the fields this integration sends are modeled;
/// the product is selected by API handle and the customer by its Maxio id.
/// </summary>
public class MaxioCreateSubscription
{
    public string? ProductHandle { get; set; }
    public long? CustomerId { get; set; }

    /// <summary>
    /// Spec schema "Collection-Method". "remittance" enrolls without capturing a card payment at
    /// signup (the customer is invoiced instead), which is what allows subscribing when the plan
    /// does not require a payment method on file.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>
    /// Our own reference for the subscription ("{customerReference}:{planHandle}"), for traceability.
    /// </summary>
    public string? Reference { get; set; }
}
