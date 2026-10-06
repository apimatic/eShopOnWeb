namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

/// <summary>
/// Data used to enroll an existing billing customer in a plan.
/// </summary>
public class NewBillingSubscription
{
    public string ProductHandle { get; init; } = string.Empty;

    public int CustomerId { get; init; }

    /// <summary>
    /// Optional payment collection override ("automatic", "remittance", "prepaid", "invoice").
    /// When null the billing system's default applies. Plans that do not require a payment
    /// method are enrolled with "remittance" so signup succeeds without card capture.
    /// </summary>
    public string? PaymentCollectionMethod { get; init; }

    /// <summary>
    /// Optional caller-supplied idempotency key; forwarded to the billing system as a
    /// uniqueness token so a retried request cannot create a second subscription.
    /// </summary>
    public string? IdempotencyKey { get; init; }
}
