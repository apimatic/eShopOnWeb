using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb identity a billing customer is created for.
/// </summary>
/// <param name="UserKey">
/// Stable, unique key for the shopper. It is the sole input to the deterministic billing customer
/// reference, so it must survive application restarts.
/// </param>
/// <param name="Email">Email address recorded on the billing customer.</param>
/// <param name="FirstName">Given name recorded on the billing customer.</param>
/// <param name="LastName">Family name recorded on the billing customer.</param>
public record Subscriber(string UserKey, string Email, string FirstName, string LastName)
{
    public string FullName => string.Join(' ', new[] { FirstName, LastName }).Trim();
}
