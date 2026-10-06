namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// Payload used to enroll an existing Maxio customer in a plan.
/// </summary>
public class CreateMaxioSubscriptionRequest
{
    public long CustomerId { get; set; }

    public long ProductId { get; set; }

    /// <summary>
    /// External id provided by this app for the subscription itself, used to
    /// make enrollment lookups idempotent.
    /// </summary>
    public string Reference { get; set; } = string.Empty;

    /// <summary>
    /// Duplicate-submission guard accepted by any POST: a retry that reaches
    /// the billing system twice within its retention window is rejected with
    /// 409 instead of creating a second subscription.
    /// </summary>
    public string UniquenessToken { get; set; } = string.Empty;

    /// <summary>
    /// How the billing system collects payment. Null lets the site default
    /// apply (automatic). Plans that do not require a payment method are
    /// enrolled with invoice-based collection ("remittance") so enrollment
    /// succeeds without card capture.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }
}
