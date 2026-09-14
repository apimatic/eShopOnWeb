namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb user, as seen by the billing system. <see cref="UserId"/> is the stable
/// identity of the shopper (the ASP.NET Identity user id) and is used as the billing-system
/// customer <c>reference</c> so that the mapping survives process restarts without any local
/// persistence, and so repeated calls are idempotent.
/// </summary>
public sealed record SubscriberIdentity(
    string UserId,
    string Email,
    string? FirstName,
    string? LastName);
