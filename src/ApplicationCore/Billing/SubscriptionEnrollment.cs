namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// Instruction for the billing gateway to enroll an existing Maxio customer on a plan.
/// </summary>
/// <param name="CustomerReference">The eShopOnWeb reference of an already existing Maxio customer.</param>
/// <param name="PlanHandle">Maxio product handle to enroll in, e.g. "eshop-pro".</param>
/// <param name="SubscriptionReference">Application supplied unique reference for the enrollment.</param>
/// <param name="PaymentCollectionMethod">One of <see cref="PaymentCollectionMethods"/>.</param>
public record SubscriptionEnrollment(
    string CustomerReference,
    string PlanHandle,
    string SubscriptionReference,
    string PaymentCollectionMethod);
