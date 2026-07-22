using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the calling identity the way the rest of eShopOnWeb does, and decides how widely a
/// caller may act: a customer only ever acts on their own subscription, an administrator on any.
/// </summary>
internal static class SubscriptionCallerExtensions
{
    public static string? UserReference(this ClaimsPrincipal user) => user.Identity?.Name;

    public static bool IsAdministrator(this ClaimsPrincipal user) =>
        user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);

    /// <summary>
    /// The reference a subscription must belong to for this caller to act on it,
    /// or null when the caller is an administrator acting across users (UC2/UC4).
    /// </summary>
    public static string? OwnershipScope(this ClaimsPrincipal user) =>
        user.IsAdministrator() ? null : user.UserReference();
}
