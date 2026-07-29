namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// The information required to enroll an eShopOnWeb user in a plan.
/// </summary>
/// <param name="UserReference">
/// A stable, unique identifier for the eShopOnWeb user (their Identity user id). Used as the
/// customer reference in the billing system so the mapping survives and stays idempotent.
/// </param>
/// <param name="Email">The user's email address.</param>
/// <param name="FirstName">First name to record on the billing customer.</param>
/// <param name="LastName">Last name to record on the billing customer.</param>
/// <param name="PlanHandle">The stable handle of the plan to subscribe to.</param>
public record SubscriptionEnrollment(
    string UserReference,
    string Email,
    string FirstName,
    string LastName,
    string PlanHandle);
