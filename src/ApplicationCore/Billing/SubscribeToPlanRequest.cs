namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>An instruction to enrol a shopper in a plan.</summary>
/// <param name="Subscriber">Who is subscribing.</param>
/// <param name="PlanHandle">Handle of a plan offered by the configured product family.</param>
/// <param name="PaymentCollectionMethod">
/// Optional override of the configured collection method (e.g. "automatic", "remittance").
/// </param>
public sealed record SubscribeToPlanRequest(
    BillingSubscriber Subscriber,
    string PlanHandle,
    string? PaymentCollectionMethod = null);
