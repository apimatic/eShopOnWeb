namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb user being billed. <see cref="UserName"/> is the stable identity carried by the
/// caller's token and is used as the billing customer's reference.
/// </summary>
public record Subscriber(string UserName, string Email);
