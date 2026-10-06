namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// Application level intent: enroll the shopper described by <see cref="Profile"/> on a plan.
/// </summary>
/// <param name="Profile">Who is subscribing (eShopOnWeb account projected onto a billing customer).</param>
/// <param name="PlanHandle">Handle of the plan to enroll in, e.g. "eshop-pro".</param>
public record SubscribeCommand(BillingProfile Profile, string PlanHandle);

/// <summary>
/// Result of a subscribe attempt.
/// </summary>
/// <param name="Subscription">The enrollment that now exists in the billing system of record.</param>
/// <param name="Customer">The Maxio customer backing the eShopOnWeb account.</param>
/// <param name="Created">
/// True when this call created the enrollment; false when an already existing enrollment was
/// returned instead. A repeated (double-clicked) subscribe is therefore a 200-style replay,
/// never a second subscription.
/// </param>
public record SubscribeOutcome(UserSubscription Subscription, BillingCustomer Customer, bool Created);
