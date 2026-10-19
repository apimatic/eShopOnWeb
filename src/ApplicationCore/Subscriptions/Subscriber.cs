namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>The eShopOnWeb user acting as a billing customer.</summary>
/// <param name="UserName">The stable eShop user name (the JWT name claim); used as the billing customer reference.</param>
/// <param name="Email">The user's e-mail address, sent to the billing system as the customer e-mail.</param>
public sealed record Subscriber(string UserName, string Email);
