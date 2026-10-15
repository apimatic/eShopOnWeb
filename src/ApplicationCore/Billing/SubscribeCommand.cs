namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <param name="BuyerId">The authenticated buyer (never taken from the request body).</param>
/// <param name="Email">The buyer's e-mail address, used for the billing customer.</param>
/// <param name="FirstName">Optional; derived from <paramref name="Email"/> when absent.</param>
/// <param name="LastName">Optional; a default is used when absent.</param>
/// <param name="PlanHandle">Handle of an offered plan.</param>
public sealed record SubscribeCommand(string BuyerId, string Email, string? FirstName, string? LastName, string PlanHandle);
