namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Request to enroll a shopper in a subscription plan.
/// </summary>
/// <param name="ShopperReference">
/// A stable, unique identifier for the shopper from eShopOnWeb (the authenticated user name / email).
/// Used as the Maxio customer <c>reference</c> so the operation is idempotent across restarts.
/// </param>
/// <param name="Email">The shopper's email address.</param>
/// <param name="FirstName">The shopper's first name (used when a Maxio customer must be created).</param>
/// <param name="LastName">The shopper's last name (used when a Maxio customer must be created).</param>
/// <param name="PlanHandle">The handle of the plan to subscribe to (e.g. <c>eshop-pro</c>).</param>
public record SubscribeCommand(
    string ShopperReference,
    string Email,
    string FirstName,
    string LastName,
    string PlanHandle);
