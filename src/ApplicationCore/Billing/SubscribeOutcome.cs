namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public enum SubscribeOutcome
{
    /// <summary>This request created the subscription.</summary>
    Created = 1,

    /// <summary>The subscription already existed for this user and plan; it is returned unchanged.</summary>
    Replayed = 2,

    /// <summary>The create may have landed but could not be confirmed yet; it is settled by a later re-read.</summary>
    OutcomePending = 3
}
