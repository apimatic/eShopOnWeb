namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum PaymentAttemptStatus
{
    // Claimed and sent (or about to be sent) to the payment provider.
    InFlight = 0,
    Authorised = 1,
    // The issuer or provider declined the card. Final; the order can be paid again.
    Refused = 2,
    // The provider rejected the request itself (invalid card data, configuration). Final; nothing was charged.
    Rejected = 3,
    // The card needs a shopper interaction (e.g. 3-D Secure) this checkout does not support. Nothing was charged.
    ActionRequired = 4,
    // The provider may have acted but its answer could not be read. Blocks further attempts until settled.
    Unknown = 5,
    // The provider accepted the payment but has not given a final result yet. Blocks further attempts.
    Pending = 6,
    // The claim lost a race against an earlier attempt and was never sent.
    Withdrawn = 7
}
