namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public enum SubscribeOutcome
{
    /// <summary>A new subscription was created.</summary>
    Created,

    /// <summary>The buyer already holds a subscription to this plan; it is returned instead of creating another.</summary>
    AlreadySubscribed,

    /// <summary>The billing system could not confirm the outcome in time; it will be settled by reference on the next read.</summary>
    PendingConfirmation
}
