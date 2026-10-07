namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum PaymentAttemptStatus
{
    /// <summary>Claimed and sent (or about to be sent) to the provider; no answer recorded yet.</summary>
    InFlight = 0,

    /// <summary>The provider authorised the full amount.</summary>
    Authorised = 1,

    /// <summary>The card or issuer declined the payment.</summary>
    Refused = 2,

    /// <summary>The provider rejected the request itself (invalid card data, configuration); nothing was charged.</summary>
    Rejected = 3,

    /// <summary>The provider holds the payment but has no final result yet.</summary>
    Pending = 4,

    /// <summary>The issuer asked for shopper interaction (for example 3D Secure) that this API cannot perform.</summary>
    ActionRequired = 5,

    /// <summary>Only part of the amount was authorised; the order is not paid and needs operator attention.</summary>
    PartiallyAuthorised = 6,

    /// <summary>The provider did not answer; the payment may or may not have been taken. Settled by re-sending with the same idempotency key.</summary>
    Unknown = 7,

    /// <summary>An unknown attempt whose settlement was handed to a later attempt carrying the same idempotency key.</summary>
    Superseded = 8
}
