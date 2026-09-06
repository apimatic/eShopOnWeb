namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb shopper a billing operation is performed on behalf of. Always derived from the
/// authenticated caller — never from request input — so a caller can only ever act on their own account.
/// </summary>
/// <param name="Email">The shopper's email address; doubles as their eShopOnWeb user name.</param>
/// <param name="FirstName">Optional given name, used only when the billing customer has to be created.</param>
/// <param name="LastName">Optional family name, used only when the billing customer has to be created.</param>
public sealed record Subscriber(string Email, string? FirstName = null, string? LastName = null);
