namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum PaymentAttemptStatus
{
    /// <summary>Recorded before the provider was called; the outcome is not known yet.</summary>
    Initiated = 0,
    Authorised = 1,
    Refused = 2,
    /// <summary>The provider rejected the request; no money was taken.</summary>
    Failed = 3,
    /// <summary>The card needs shopper authentication this checkout cannot perform; no money was taken.</summary>
    ActionRequired = 4,
    /// <summary>The provider accepted the payment but has not reached a final result.</summary>
    Pending = 5,
    /// <summary>The provider did not answer; the payment may or may not have been taken.</summary>
    Unknown = 6
}
