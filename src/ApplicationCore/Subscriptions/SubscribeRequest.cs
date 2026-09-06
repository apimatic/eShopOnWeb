namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A request to enroll an eShopOnWeb user in a recurring plan.
/// </summary>
/// <param name="UserName">The eShopOnWeb user name (an e-mail address) taken from the caller's token. Never client supplied.</param>
/// <param name="PlanHandle">Handle of the plan to subscribe to, or <c>null</c> to use the configured default plan.</param>
public record SubscribeRequest(string UserName, string? PlanHandle = null);
