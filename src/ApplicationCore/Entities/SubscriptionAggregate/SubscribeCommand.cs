namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Inputs required to enroll a signed-in shopper into a subscription plan.
/// </summary>
/// <param name="UserId">
/// The eShopOnWeb user's stable unique identifier. It is used as the Maxio customer
/// <c>reference</c>, which guarantees one Maxio customer per shop user (idempotent enrollment).
/// </param>
/// <param name="Email">The user's email address, used for the Maxio customer record.</param>
/// <param name="PlanHandle">The Maxio product handle of the plan to subscribe to.</param>
public record SubscribeCommand(string UserId, string Email, string PlanHandle);
