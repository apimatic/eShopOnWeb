namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A request to enroll an eShopOnWeb user in a subscription plan. The identity
/// fields are derived from the authenticated caller, never from client input.
/// </summary>
public record SubscribeCommand(
    string CustomerReference,
    string Email,
    string FirstName,
    string LastName,
    string PlanHandle);
