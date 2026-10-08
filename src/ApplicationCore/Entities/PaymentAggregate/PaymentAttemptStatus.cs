namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public enum PaymentAttemptStatus
{
    /// <summary>Claimed and sent (or about to be sent) to the payment provider.</summary>
    Processing,
    /// <summary>Authorised with immediate capture: the money is taken.</summary>
    Authorised,
    /// <summary>The provider declined the card. Nothing was charged.</summary>
    Refused,
    /// <summary>The attempt failed before or at the provider without charging the shopper.</summary>
    Failed,
    /// <summary>The provider accepted the payment but has not reported a final result yet.</summary>
    Pending,
    /// <summary>The call to the provider failed in a way that leaves the outcome unknown; it must be settled.</summary>
    Unknown
}
