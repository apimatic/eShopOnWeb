namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>Attributes used to create a subscription at the billing provider.</summary>
public class NewSubscription
{
    public NewSubscription(long customerId, string planHandle, string reference, string? paymentCollectionMethod)
    {
        CustomerId = customerId;
        PlanHandle = planHandle;
        Reference = reference;
        PaymentCollectionMethod = paymentCollectionMethod;
    }

    public long CustomerId { get; }

    public string PlanHandle { get; }

    /// <summary>
    /// Unique, caller-generated reference stored on the subscription. The provider rejects a second
    /// create with the same reference, which is what makes concurrent duplicate creates impossible.
    /// </summary>
    public string Reference { get; }

    /// <summary>
    /// How the subscription is collected (<c>automatic</c>, <c>remittance</c>, <c>invoice</c>,
    /// <c>prepaid</c>). <c>null</c> leaves the billing site default in force.
    /// </summary>
    public string? PaymentCollectionMethod { get; }
}
